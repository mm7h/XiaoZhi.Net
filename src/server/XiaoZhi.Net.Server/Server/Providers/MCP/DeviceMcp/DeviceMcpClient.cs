using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.MCP.DeviceMcp
{
    internal class DeviceMcpClient : BaseMcpClient<DeviceMcpClient>, ISubMcpClient
    {
        private string? _visionUrl;
        private string? _visionToken;

        public DeviceMcpClient(ILogger<DeviceMcpClient> logger) : base(logger)
        {
        }

        public override string ModelName => SubMCPClientTypeNames.DeviceMcpClient;
        public override string ProviderType => "SubMcpClient";

        public override bool Build(MCPClientBuildConfig config)
        {
            this.InitSession(config);
            ModelSetting modelSetting = config.ModelSetting;

            this._visionUrl = modelSetting.Config.GetConfigValueOrDefault("VisionUrl", string.Empty);
            this._visionToken = modelSetting.Config.GetConfigValueOrDefault("VisionToken", string.Empty);




            this.SendMcpInitializeAsync().ConfigureAwait(false);
            this.RequestToolsListAsync().ConfigureAwait(false);

            return true;
        }

        public override async Task SendMcpInitializeAsync()
        {

            var vision = new
            {
                Url = this._visionUrl,
                Token = this._visionToken
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
                Method = RequestMethods.Initialize,
                Id = new RequestId(1),
                Params = @params.ToNode()
            };
            this.Logger.LogInformation(Lang.DeviceMcpClient_SendMcpInitializeAsync_SendingInit, this.CurrentSession.SessionId);
            await this.SendMCPMessageAsync(request);
        }

        protected override async Task SendMCPMessageAsync<TMessage>(TMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "Message cannot be null.");
            }
            var mcpMessage = new
            {
                Type = "mcp",
                Payload = message
            };
            string jsonMessage = mcpMessage.ToJson();
            await this.CurrentSession.SendOutter.SendAsync(jsonMessage);
        }

        public override void Dispose()
        {

        }
    }
}
