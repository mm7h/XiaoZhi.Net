using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Providers.MCP.McpEndpoint
{
    internal class McpEndpointClient : BaseMcpClient<McpEndpointClient>, ISubMcpClient
    {
        private string? _endpointUrl;
        private WebSocketClient? _webSocketClient;

        public McpEndpointClient(ILogger<McpEndpointClient> logger) : base(logger)
        {
        }

        public override string ModelName => SubMCPClientTypeNames.DeviceMcpClient;
        public override string ProviderType => "SubMcpClient";

        public override bool Build(MCPClientBuildConfig config)
        {
            try
            {
                this.InitSession(config);
                ModelSetting modelSetting = config.ModelSetting;

                this._endpointUrl = modelSetting.Config.GetConfigValueOrDefault("EndpointUrl");

                if (string.IsNullOrWhiteSpace(this._endpointUrl))
                {
                    this.Logger.LogWarning(Lang.McpEndpointClient_Build_UrlEmpty);
                    return true;
                }

                Dictionary<string, string>? headers = modelSetting.Config.GetConfigValueOrDefault<Dictionary<string, string>>("Headers");
                this._webSocketClient = new WebSocketClient(headers);
                this._webSocketClient.OnOpen += this.WebSocketClientEngine_OnOpenAsync;
                this._webSocketClient.OnTextMessage += this.WebSocketClient_OnMessageAsync;

                this._webSocketClient.ConnectAsync(this._endpointUrl).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.McpEndpointClient_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }
        }


        protected override Task SendMCPMessageAsync<TMessage>(TMessage message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message), "Message cannot be null.");
            }

            if (this._webSocketClient is null)
            {
                throw new InvalidOperationException("WebSocket client is not initialized.");
            }

            string json = message.ToJson();
            return this._webSocketClient.SendAsync(json);
        }

        public override void Dispose()
        {
            if (this._webSocketClient is null)
            {
                return;
            }
            this._webSocketClient.CloseAsync().ConfigureAwait(false);
        }
        private async void WebSocketClientEngine_OnOpenAsync()
        {
            await this.SendMcpInitializeAsync();
            await this.SendMcpNotificationAsync(NotificationMethods.InitializedNotification);
            await this.RequestToolsListAsync();

            this.Logger.LogInformation(Lang.McpEndpointClient_OnOpen_Connected);
        }

        private async void WebSocketClient_OnMessageAsync(string data)
        {
            await this.HandleMcpEndpointMessageAsync(data);
        }

        private async Task HandleMcpEndpointMessageAsync(string data)
        {
            try
            {
                JsonObject? jObj = JsonNode.Parse(data) as JsonObject;
                if (jObj is null)
                {
                    return;
                }
                await this.HandleMcpMessageAsync(jObj);
            }
            catch (Exception)
            {

                throw;
            }
        }
    }
}
