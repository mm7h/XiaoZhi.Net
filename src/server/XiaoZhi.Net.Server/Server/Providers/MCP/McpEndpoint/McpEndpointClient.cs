using ModelContextProtocol.Protocol;
using Serilog;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Providers.MCP.McpEndpoint
{
    internal class McpEndpointClient : BaseMcpClient
    {
        private readonly string? _endpointUrl;
        private readonly WebSocketClientEngine _webSocketClientEngine;

        public McpEndpointClient(Session session, ModelSetting mcpSetting, ILogger logger) : base(session, mcpSetting, logger)
        {
            this._endpointUrl = this.ModelSetting?.Config?.EndpointUrl;
            this._webSocketClientEngine = new WebSocketClientEngine(this._endpointUrl, this.ModelSetting?.Config?.Headers);
            this._webSocketClientEngine.OnOpen += this.WebSocketClientEngine_OnOpen;
            this._webSocketClientEngine.OnMessage += this.WebSocketClient_OnMessage;
        }

        public override string ProviderType => "mcp_end_point";

        public override bool Build()
        {
            if (string.IsNullOrWhiteSpace(this._endpointUrl))
            {
                this.Logger.Error("Endpoint URL cannot be null or empty.");
                return false;
            }

            return this._webSocketClientEngine.Connect();
        }


        protected override Task SendMCPMessageAsync<TMessage>(TMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "Message cannot be null.");
            }
            string json = message.ToJson();
            return this._webSocketClientEngine.SendAsync(json);
        }

        public override void Dispose()
        {
            this._webSocketClientEngine.Close();
        }
        private async void WebSocketClientEngine_OnOpen()
        {
            await this.SendMcpInitializeAsync("XiaozhiMCPEndpointClient");
            await this.SendMcpNotificationAsync(NotificationMethods.InitializedNotification);
            await this.RequestToolsListAsync();

            this.Logger.Information("MCP Endpoint Client connected and initialized successfully.");
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
