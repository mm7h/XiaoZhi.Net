using Serilog;
using System;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class TextHandler : BaseHandler, IOutHandler<string>
    {
        public TextHandler(XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
        }
        public event Action<Session>? OnManualStop;
        public override string HandlerName => nameof(TextHandler);
        public ISendOutter SendOutter { get; set; } = null!;
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

            this.Logger.Debug("Received text from client: {jsonText}", jsonObject?.ToJsonString());

            if (jsonObject is JsonObject jsonObj)
            {
                string? type = jsonObj["type"]?.GetValue<string>()?.ToLower();
                if (string.IsNullOrEmpty(type))
                {
                    this.Logger.Error("Invalid type for text message handle.");
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
                        this.HandleIotDescriptors();
                        break;
                    case "mcp":
                        _ = Task.Run(() =>
                        {
                            this.HandleMcp(jsonObj);
                        });

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
                if (audioParamsObj.TryGetPropertyValue("format", out var format) && format is not null)
                {
                    string formatValue = format.GetValue<string>();
                    if (!string.IsNullOrEmpty(formatValue))
                    {
                        session.AudioFormat = formatValue;
                    }
                }
            }

            if (jsonObj.TryGetPropertyValue("features", out var features) && features is not null)
            {
                JsonObject featuresObj = features.AsObject();
                if (featuresObj.TryGetPropertyValue("mcp", out var mcp) && mcp is not null)
                {
                    bool isSupportMCP = mcp.GetValue<bool>();
                    if (isSupportMCP)
                    {
                        session.IsSupportMCP = true;
                    }
                }
            }
            this.SendOutter.SendAsync(JsonHelper.Serialize(defultHelloMessage));
        }

        private async Task HandleAbortMessage()
        {
            Session session = this.SendOutter.GetSession();
            this.Logger.Information("Abort message received");
            await this.SendOutter.SendAbortMessageAsync();
            session.Abort();
            this.Logger.Information("Abort message received-end, cancelled the tasks.");
        }

        private async void HandleListen(JsonObject jsonObject)
        {
            Session session = this.SendOutter.GetSession();
            string? mode = jsonObject["mode"]?.GetValue<string>()?.ToLower();
            if (!string.IsNullOrEmpty(mode))
            {
                session.SetListenMode(mode);
                this.Logger.Information("Client voice listening mode setting is: {mode}", mode);
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
                    if (session.CheckAsrData())
                    {
                        this.OnManualStop?.Invoke(session);
                    }
                }
                else if (state == "detect")
                {
                    // 用于客户端向服务器告知检测到唤醒词
                    string? text = jsonObject["text"]?.GetValue<string>()?.ToLower();
                    if (!string.IsNullOrEmpty(text))
                    {
                        // startToChat
                        await this.NextWriter!.WriteAsync(new Workflow<string>(session, text));
                    }
                }
            }
        }

        private void HandleIotDescriptors()
        {

        }

        private async void HandleMcp(JsonObject jsonObject)
        {
            
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}
