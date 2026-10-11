using System;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Providers.MCP;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class TextHandler : BaseHandler, IOutHandler<string>
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
        public ChannelWriter<Workflow<string>> NextWriter { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            this.RegisterCancellationToken();
            this.Builded = true;
            return true;
        }

        public async Task HandleAsync(string data)
        {
            Session session = this.SendOutter.GetSession();
            if (session.CloseAfterChat)
            {
                return;
            }

            JsonNode? jsonObject = JsonNode.Parse(data);

            // 判断是否是整数
            if (jsonObject is JsonValue jsonValue && jsonValue.TryGetValue(out int intValue))
            {
                await this.SendOutter.SendAsync(intValue.ToString());
                return;
            }

            if (jsonObject is JsonObject jsonObj)
            {
                string? type = jsonObj["type"]?.GetValue<string>()?.ToLower();
                if (string.IsNullOrWhiteSpace(type))
                {
                    this.Logger.LogError(Lang.TextHandler_Handle_InvalidType);
                    return;
                }

                switch (type)
                {
                    case "abort":
#if DEBUG
                        this.Logger.LogDebug(Lang.TextHandler_Handle_ReceivedText, jsonObject?.ToJsonString());
#endif
                        await this.HandleAbortMessageAsync();
                        break;
                    case "listen":
#if DEBUG
                        this.Logger.LogDebug(Lang.TextHandler_Handle_ReceivedText, jsonObject?.ToJsonString());
#endif
                        await this.HandleListenAsync(jsonObj);
                        break;
                    case "iot":
                        this.HandleIotDescriptors(jsonObj);
                        break;
                    case "mcp":
                        await Task.Run(() =>
                        {
                            this.HandleMcpAsync(jsonObj);
                        });

                        break;
                }
            }
        }

        private async Task HandleAbortMessageAsync()
        {
            Session session = this.SendOutter.GetSession();
            this.Logger.LogInformation(Lang.TextHandler_HandleAbortMessage_Received);
            // 音乐使用独立播放令牌，中止对话前先暂停音源，避免清空混音后立即重新填充。
            if (session.PrivateProvider.AudioPlayerClient is { IsPlaying: true } player)
            {
                await player.MusicPlayer.PauseAsync();
            }
            session.Abort();
            await this.SendOutter.SendAbortMessageAsync();
            this.Logger.LogInformation(Lang.TextHandler_HandleAbortMessage_Cancelled);
        }

        private async Task HandleListenAsync(JsonObject jsonObject)
        {
            Session session = this.SendOutter.GetSession();
            if (session.CloseAfterChat)
            {
                return;
            }

            string? mode = jsonObject["mode"]?.GetValue<string>()?.ToLower();
            if (!string.IsNullOrWhiteSpace(mode))
            {
                session.SetListenMode(mode);
                this.Logger.LogInformation(Lang.TextHandler_HandleListen_ModeSetting, mode);
            }

            string? state = jsonObject["state"]?.GetValue<string>()?.ToLower();
            if (!string.IsNullOrWhiteSpace(state))
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
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (session.PrivateProvider.AudioPlayerClient?.IsPlaying == true)
                        {
                            await this.HandleAbortMessageAsync();
                        }
                        var workflow = this._workflowPool.Get();
                        workflow.Initialize(session, text);
                        // 客户端在中止后自动发送 listen start，不能使正在处理的文本轮次失效。
                        session.BeginTextProcessing(workflow.TurnId);
                        try
                        {
                            await this.NextWriter.WriteAsync(workflow, this.HandlerToken);
                        }
                        catch
                        {
                            session.CompleteTextProcessing(workflow.TurnId);
                            this._workflowPool.Return(workflow);
                            throw;
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

            if (session.PrivateProvider.IoTClient is null)
            {
                this.Logger.LogError(Lang.TextHandler_HandleIotDescriptors_ClientNotInit, session.DeviceId);
                return;
            }
            session.PrivateProvider.IoTClient.HandleIoTMessage(jsonObject);
        }

        private async void HandleMcpAsync(JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("payload", out var payload) && payload is not null && payload is JsonObject payloadObj)
            {
                Session session = this.SendOutter.GetSession();
                ISubMcpClient? subMcpClient = session.PrivateProvider.McpClient?.GetSubMcpClient(SubMCPClientTypeNames.DeviceMcpClient);
                if (subMcpClient is not null)
                {
                    await subMcpClient.HandleMcpMessageAsync(payloadObj);
                }
                else
                {
                    this.Logger.LogError(Lang.TextHandler_HandleMcp_ClientNotFound, session.SessionId);
                }
            }

        }

        public override void Dispose()
        {
            if (this.IsDisposed)
            {
                return;
            }
            this.NextWriter?.TryComplete();
            base.Dispose();
        }
    }
}
