using ModelContextProtocol.Protocol;
using Serilog;
using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.MCP.DeviceMcp
{
    internal class DeviceMcpClient : BaseMcpClient
    {
        public DeviceMcpClient(Session session, ILogger logger) : base(session, logger)
        {

        }

        public override string ProviderType => "device mcp";

        public override bool Build()
        {
            return true;
        }

        protected override Task SendMcpInitialize(string clientName)
        {
            if (string.IsNullOrEmpty(clientName))
            {
                clientName = "DeviceMcpClient";
            }

            var vision = new 
            {
                Url = "",
                Token = ""
            };

            var @params = new
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new
                {
                    Roots = new 
                    {
                        ListChanged = true
                    },
                    Sampling = new { },
                    Vision = vision
                },
                clientInfo = new 
                {
                    Name = clientName,
                    Version = "1.0.0"
                }
            };

            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = RequestMethods.ToolsList,
                Id = new RequestId(1),
                Params = @params.ToNode()
            };
            this.Logger.Information("Session {sessionId} sending MCP Initialize request.", this.CurrentSession.SessionId);
            return this.SendMCPMessage(request);
        }

        protected override Task SendMCPMessage<TMessage>(TMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "Message cannot be null.");
            }
            string jsonMessage = message.ToJson();
            return this.CurrentSession.SendOutter.SendAsync(jsonMessage);
        }

        public override void Dispose()
        {

        }
    }
}
