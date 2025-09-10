using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers.MCP;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class TextHandler : BaseHandler, IOutHandler<string>
    {
        private readonly ProviderManager _providerManager;
        private readonly ObjectPool<Workflow<string>> _workflowPool;

        public TextHandler(ProviderManager providerManager, 
            ObjectPool<Workflow<string>> workflowPool,
            XiaoZhiConfig config, 
            ILogger<TextHandler> logger) : base(config, logger)
        {
            this._providerManager = providerManager;
            this._workflowPool = workflowPool;
        }
        
        public event Action<Session>? OnManualStop;
        public override string HandlerName => nameof(TextHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelWriter<Workflow<string>> NextWriter { get; set; } = null!;

        public async void Handle(string data)
        {
            JsonNode? jsonObject = JsonNode.Parse(data);

            // 判断是否是整数
            if (jsonObject is JsonValue jsonValue && jsonValue.TryGetValue(out int intValue))
            {
                await this.SendOutter.SendAsync(intValue.ToString());
                return;
            }

#if DEBUG
            this.Logger.LogDebug("Received text from client: {jsonText}", jsonObject?.ToJsonString());
#endif

            if (jsonObject is JsonObject jsonObj)
            {
                string? type = jsonObj["type"]?.GetValue<string>()?.ToLower();
                if (string.IsNullOrEmpty(type))
                {
                    this.Logger.LogError("Invalid type for text message handle.");
                    return;
                }

                switch (type)
                {
                    case "hello":
                        this.HandleHelloMessage(jsonObj);
                        break;
                    case "abort":
                        await this.HandleAbortMessage();
                        break;
                    case "listen":
                        this.HandleListen(jsonObj);
                        break;
                    case "iot":
                        this.HandleIotDescriptors(jsonObj);
                        break;
                    case "mcp":
                        await Task.Run(() =>
                        {
                            this.HandleMcp(jsonObj);
                        }).ConfigureAwait(false);

                        break;
                }
            }
        }

        private void HandleHelloMessage(JsonObject jsonObj)
        {
            Session session = this.SendOutter.GetSession();

            AudioParams defaultAudioParams = new AudioParams(this.Config.AudioSetting.SampleRate, this.Config.AudioSetting.Channels, this.Config.AudioSetting.FrameDuration);
            HelloMessage defultHelloMessage = new HelloMessage(this.SendOutter.SessionId, this.Config.ServerProtocol.GetDescription().ToLower(), defaultAudioParams);

            if (jsonObj.TryGetPropertyValue("audio_params", out var audioParams) && audioParams is not null)
            {
                JsonObject audioParamsObj = audioParams.AsObject();
                string format = audioParamsObj["format"]?.GetValue<string>() ?? "opus";
                int sampleRate = audioParamsObj["sample_rate"]?.GetValue<int>() ?? 16000;
                int channels = audioParamsObj["channels"]?.GetValue<int>() ?? 1;
                int frameDuration = audioParamsObj["frame_duration"]?.GetValue<int>() ?? 60;

                session.AudioSetting.Format = format;
                session.AudioSetting.SampleRate = sampleRate;
                session.AudioSetting.Channels = channels;
                session.AudioSetting.FrameDuration = frameDuration;

                this._providerManager.BuildAudioPlayer(session);
                this._providerManager.BuildAudioMixer(session);
                this._providerManager.RegisterAudioResampler(session);
                this._providerManager.RegisterAudioEncoder(session);
            }

            this.SendOutter.SendAsync(JsonHelper.Serialize(defultHelloMessage));

            if (jsonObj.TryGetPropertyValue("features", out var features) && features is not null)
            {
                JsonObject featuresObj = features.AsObject();
                if (featuresObj.TryGetPropertyValue("mcp", out var mcp) && mcp is not null)
                {
                    bool isSupportMCP = mcp.GetValue<bool>();
                    if (isSupportMCP)
                    {
                        this._providerManager.BuildMCP(session);
                    }
                }
            }
        }

        private async Task HandleAbortMessage()
        {
            Session session = this.SendOutter.GetSession();
            this.Logger.LogInformation("Abort message received");
            await this.SendOutter.SendAbortMessageAsync();
            session.Abort();
            this.Logger.LogInformation("Abort message received-end, cancelled the tasks.");
        }

        private async void HandleListen(JsonObject jsonObject)
        {
            Session session = this.SendOutter.GetSession();
            string? mode = jsonObject["mode"]?.GetValue<string>()?.ToLower();
            if (!string.IsNullOrEmpty(mode))
            {
                session.SetListenMode(mode);
                this.Logger.LogInformation("Client voice listening mode setting is: {mode}", mode);
            }

            string? state = jsonObject["state"]?.GetValue<string>()?.ToLower();
            if (!string.IsNullOrEmpty(state))
            {
                if (state == "start")
                {
                    session.ManualStart();
                }
                else if (state == "stop")
                {
                    session.ManualStop();
                    this.OnManualStop?.Invoke(session);
                }
                else if (state == "detect")
                {
                    string? text = jsonObject["text"]?.GetValue<string>()?.ToLower();
                    if (!string.IsNullOrEmpty(text))
                    {
                        var workflow = this._workflowPool.Get();
                        try
                        {
                            workflow.Initialize(session, text);
                            await this.NextWriter.WriteAsync(workflow);
                        }
                        finally
                        {
                            this._workflowPool.Return(workflow);
                        }
                    }
                }
            }
        }

        private void HandleIotDescriptors(JsonObject jsonObject)
        {
            Session session = this.SendOutter.GetSession();
            if (!session.PrivateProvider.HasIoT)
            {
                this._providerManager.BuildIoT(session);
            }

            session.PrivateProvider.IoTClient.HandleIoTMessage(jsonObject);
        }

        private async void HandleMcp(JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("payload", out var payload) && payload is not null && payload is JsonObject payloadObj)
            {
                Session session = this.SendOutter.GetSession();
                ISubMcpClient? subMcpClient = session.PrivateProvider.McpClient.GetSubMcpClient(SubMCPClientTypeNames.DeviceMcpClient);
                if (subMcpClient is not null)
                {
                    await subMcpClient.HandleMcpMessageAsync(payloadObj);
                }
                else
                {
                    this.Logger.LogError("DeviceMcpClient not found in session {sessionId}.", session.SessionId);
                }
            }

        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}
