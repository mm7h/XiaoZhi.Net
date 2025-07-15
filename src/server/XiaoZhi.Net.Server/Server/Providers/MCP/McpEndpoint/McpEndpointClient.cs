using ModelContextProtocol.Protocol;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Providers.MCP.McpEndpoint
{
    internal class McpEndpointClient : BaseMcpClient
    {
        private readonly string _endpointUrl;
        private readonly WebSocketClientEngine _webSocketClientEngine;

        public McpEndpointClient(string endpointUrl, IDictionary<string, string>? headers, Session session, ILogger logger) : base(session, logger)
        {
            this._endpointUrl = endpointUrl;
            this._webSocketClientEngine = new WebSocketClientEngine(this._endpointUrl, headers);
            this._webSocketClientEngine.OnOpen += this.WebSocketClientEngine_OnOpen;
            this._webSocketClientEngine.OnMessage += this.WebSocketClient_OnMessage;
        }

        public override string ProviderType => "mcp end point";

        public override bool Build()
        {
            if (string.IsNullOrWhiteSpace(this._endpointUrl))
            {
                this.Logger.Error("Endpoint URL cannot be null or empty.");
                return false;
            }

            return this._webSocketClientEngine.Connect();
        }


        protected override Task SendMCPMessage<TMessage>(TMessage message)
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

        }
        private async void WebSocketClientEngine_OnOpen()
        {
            await this.SendMcpInitialize("XiaozhiMCPEndpointClient");
            await this.SendMcpNotification(NotificationMethods.InitializedNotification);
            await this.RequestToolsList();

            this.Logger.Information("MCP Endpoint Client connected and initialized successfully.");
        }

        private void WebSocketClient_OnMessage(string data)
        {
            try
            {
                this.HandleMcpEndpointMessage(data);
            }
            catch (Exception)
            {

                throw;
            }
        }

        private void HandleMcpEndpointMessage(string data)
        {

        }
    }
}
