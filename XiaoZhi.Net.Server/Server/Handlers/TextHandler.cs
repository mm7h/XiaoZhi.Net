using Serilog;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;

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

            this.Logger.Debug($"Received text from client: {jsonObject?.ToJsonString()}");

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
                        this.HandleHelloMessage(connId);
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
                }
            }
        }

        private void HandleHelloMessage(string connId)
        {
            Session session = this._protocolEngine.GetSessionContext(connId);
            var helloMessage = new
            {
                type = "hello",
                version = 1,
                transport = this.Config.ServerProtocol.GetDescription().ToLower(),
                session_id = session.SessionId,
                audio_params = new
                {
                    format = "opus",
                    sample_rate = this.Config.AudioSetting.SampleRate,
                    channels = this.Config.AudioSetting.Channels,
                    frame_duration = this.Config.AudioSetting.FrameDuration
                }
            };
            this._protocolEngine.SendAsync(connId, JsonSerializer.Serialize(helloMessage));
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
            await this._protocolEngine.SendAsync(connId, JsonSerializer.Serialize(abortMessage));
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
                this.Logger.Information($"Client voice listening mode setting is: {mode}");
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

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}
