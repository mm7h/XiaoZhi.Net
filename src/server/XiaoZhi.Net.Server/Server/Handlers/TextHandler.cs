using Serilog;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using static System.Collections.Specialized.BitVector32;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class TextHandler : BaseHandler, IOutHandler<string>
    {
        private readonly IProtocolEngine _protocolEngine;
        public TextHandler(IProtocolEngine protocolEngine, XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
            this._protocolEngine = protocolEngine;
        }
        public event Action<Session> OnManualStop;
        public override string HandlerName => nameof(TextHandler);
        public ChannelWriter<Workflow<string>> NextWriter { get; set; }

        public async void Handle(string connId, string data)
        {
            JsonNode? jsonObject = JsonNode.Parse(data);

            // 判断是否是整数
            if (jsonObject is JsonValue jsonValue && jsonValue.TryGetValue(out int intValue))
            {
                await this._protocolEngine.SendAsync(connId, intValue.ToString());
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
                        this.HandleHelloMessage(connId, jsonObj);
                        break;
                    case "abort":
                        await this.HandleAbortMessage(connId);
                        break;
                    case "listen":
                        this.HandleListen(connId, jsonObj);
                        break;
                    case "iot":
                        this.HandleIotDescriptors(connId);
                        break;
                    case "mcp":
                        this.HandleMcp(connId, jsonObj);
                        break;
                }
            }
        }

        private void HandleHelloMessage(string connId, JsonObject jsonObj)
        {
            Session session = this._protocolEngine.GetSessionContext(connId);

            AudioParams defaultAudioParams = new AudioParams(this.Config.AudioSetting.SampleRate, this.Config.AudioSetting.Channels, this.Config.AudioSetting.FrameDuration);
            HelloMessage defultHelloMessage = new HelloMessage(connId, this.Config.ServerProtocol.GetDescription().ToLower(), defaultAudioParams);

            if (jsonObj.TryGetPropertyValue("audio_params", out var audioParams) && audioParams != null)
            {
                JsonObject audioParamsObj = audioParams.AsObject();
                if (audioParamsObj.TryGetPropertyValue("format", out var format) && format != null)
                {
                    string formatValue = format.GetValue<string>();
                    if (!string.IsNullOrEmpty(formatValue))
                    {
                        session.AudioFormat = formatValue;
                    }
                }
            }

            if (jsonObj.TryGetPropertyValue("features", out var features) && features != null)
            {
                JsonObject featuresObj = features.AsObject();
                if (featuresObj.TryGetPropertyValue("mcp", out var mcp) && mcp != null)
                {
                    bool isSupportMCP = mcp.GetValue<bool>();
                    if (isSupportMCP)
                    {
                        session.IsSupportMCP = true;
                    }
                }
            }
            this._protocolEngine.SendAsync(connId, JsonHelper.Serialize(defultHelloMessage));
        }

        private async Task HandleAbortMessage(string connId)
        {
            Session session = this._protocolEngine.GetSessionContext(connId);
            this.Logger.Information("Abort message received");
            var abortMessage = new
            {
                type = "tts",
                state = "stop",
                session_id = session.SessionId
            };
            await this._protocolEngine.SendAsync(connId, JsonHelper.Serialize(abortMessage));
            session.Abort();
            this.Logger.Information("Abort message received-end, cancelled the tasks.");
        }

        private async void HandleListen(string connId, JsonObject jsonObject)
        {
            Session session = this._protocolEngine.GetSessionContext(connId);
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
                        this.OnManualStop.Invoke(session);
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

        private void HandleIotDescriptors(string connId)
        {

        }

        private void HandleMcp(string connId, JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("result", out var result) && result != null)
            {
                int msgId =  result["id"]?.AsValue().GetValue<int>() ?? 0;


                if (msgId == 1)
                {
                    // mcp initialize id
                    this.Logger.Information("Received MCP Initialize message from client: {connId}", connId);
                    if (result.AsObject().TryGetPropertyValue("serverInfo", out var serverInfo) && serverInfo != null)
                    {
                        string? name = serverInfo["name"]?.GetValue<string>();
                        string? version = serverInfo["version"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version))
                        {
                            this.Logger.Information("The server info from xiaozhi client MCP: name - {name}, version - {version}", name, version);
                        }
                        else
                        {
                            this.Logger.Warning("Invalid server info received from xiaozhi client MCP.");
                        }
                    }

                    return;
                }
                else if (msgId == 2)
                {
                    // mcp tools list id
                }
            }
            else if (jsonObject.TryGetPropertyValue("method", out var method) && method != null)
            {

            }
            else if (jsonObject.TryGetPropertyValue("error", out var error) && error != null)
            {

            }
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}
