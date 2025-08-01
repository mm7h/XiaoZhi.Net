using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Providers.MCP.McpEndpoint
{
    internal class McpEndpointClient : BaseMcpClient, ISubMcpClient
    {
        private readonly string? _endpointUrl;
        private readonly WebSocketClient _webSocketClient;

        public McpEndpointClient(Session session, ModelSetting mcpSetting, ILogger logger) : base(session, mcpSetting, logger)
        {
            this._endpointUrl = this.ModelSetting?.Config?.EndpointUrl;
            this._webSocketClient = new WebSocketClient(this._endpointUrl, this.ModelSetting?.Config?.Headers);
            this._webSocketClient.OnOpen += this.WebSocketClientEngine_OnOpen;
            this._webSocketClient.OnTextMessage += this.WebSocketClient_OnMessage;
        }

        public override string ProviderType => SubMCPClientTypeNames.DeviceMcpClient;

        public override bool Build()
        {
            if (string.IsNullOrEmpty(this._endpointUrl))
            {
                this.Logger.LogWarning("Endpoint URL is empty, skip this mcp tpye.");
                return true;
            }
            this._webSocketClient.ConnectAsync().ConfigureAwait(false);
            return true;
        }


        protected override Task SendMCPMessageAsync<TMessage>(TMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "Message cannot be null.");
            }
            string json = message.ToJson();
            return this._webSocketClient.SendAsync(json);
        }

        public override void Dispose()
        {
            this._webSocketClient.CloseAsync().ConfigureAwait(false);
        }
        private async void WebSocketClientEngine_OnOpen()
        {
            await this.SendMcpInitializeAsync();
            await this.SendMcpNotificationAsync(NotificationMethods.InitializedNotification);
            await this.RequestToolsListAsync();

            this.Logger.LogInformation("MCP Endpoint Client connected and initialized successfully.");
        }

        private async void WebSocketClient_OnMessage(string data)
        {
            await this.HandleMcpEndpointMessage(data);
        }

        private async Task HandleMcpEndpointMessage(string data)
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
