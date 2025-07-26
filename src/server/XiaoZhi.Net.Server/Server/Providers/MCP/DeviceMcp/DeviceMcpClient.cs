using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.MCP.DeviceMcp
{
    internal class DeviceMcpClient : BaseMcpClient
    {
        private readonly string visionUrl;
        private readonly string visionToken;

        public DeviceMcpClient(Session session, ModelSetting mcpSetting, ILogger logger) : base(session, mcpSetting, logger)
        {
            this.visionUrl = this.ModelSetting?.Config?.VisionUrl ?? "";
            this.visionToken = this.ModelSetting?.Config?.VisionToken ?? "";
        }

        public override string ProviderType => SubMCPClientTypeNames.DeviceMcpClient;

        public override bool Build()
        {
            return true;
        }

        public override Task SendMcpInitializeAsync()
        {

            var vision = new
            {
                Url = this.visionUrl,
                Token = this.visionToken
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
                    Name = this.ProviderType,
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
            this.Logger.LogInformation("Session {sessionId} sending MCP Initialize request.", this.CurrentSession.SessionId);
            return this.SendMCPMessageAsync(request);
        }

        protected override Task SendMCPMessageAsync<TMessage>(TMessage message)
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
