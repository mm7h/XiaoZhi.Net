using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;

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
            this.InitSession(config);
            ModelSetting modelSetting = config.ModelSetting;

            this._endpointUrl = modelSetting?.Config?.EndpointUrl;

            if (string.IsNullOrEmpty(this._endpointUrl))
            {
                this.Logger.LogWarning("Endpoint URL is empty, skip this mcp tpye.");
                return true;
            }

            this._webSocketClient = new WebSocketClient(this._endpointUrl, modelSetting?.Config?.Headers);
            this._webSocketClient.OnOpen += this.WebSocketClientEngine_OnOpen;
            this._webSocketClient.OnTextMessage += this.WebSocketClient_OnMessage;

            this._webSocketClient.ConnectAsync().ConfigureAwait(false);
            return true;
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
