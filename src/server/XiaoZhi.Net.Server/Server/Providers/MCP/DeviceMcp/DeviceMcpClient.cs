using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using XiaoZhi.Net.Server.Abstractions.Mcp;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.MCP.DeviceMcp
{
    internal class DeviceMcpClient : BaseMcpClient<DeviceMcpClient>, ISubMcpClient
    {
        private readonly IEnumerable<IDeviceMcpCapabilityContributor> _capabilityContributors;
        private readonly List<IDeviceMcpCapabilityLease> _capabilityLeases = [];
        private readonly Dictionary<string, object?> _capabilities = new(StringComparer.OrdinalIgnoreCase);
        private int _initialized;

        public DeviceMcpClient(IEnumerable<IDeviceMcpCapabilityContributor> capabilityContributors, ILogger<DeviceMcpClient> logger) : base(logger)
        {
            this._capabilityContributors = capabilityContributors;
        }

        public override string ModelName => SubMCPClientTypeNames.DeviceMcpClient;
        public override string ProviderType => "SubMcpClient";

        public override bool Build(MCPClientBuildConfig config)
        {
            this.ReleaseCapabilities();
            this.InitSession(config);
            this._capabilities.Clear();
            this._initialized = 0;
            this._capabilities["roots"] = new
            {
                listChanged = true
            };
            this._capabilities["sampling"] = new { };

            try
            {
                DeviceMcpCapabilityContext context = new DeviceMcpCapabilityContext(config.Session.SessionId, config.Session.DeviceId);
                foreach (IDeviceMcpCapabilityContributor contributor in this._capabilityContributors)
                {
                    IDeviceMcpCapabilityLease? lease = contributor.CreateCapability(context);
                    if (lease is null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(lease.CapabilityName)
                        || lease.Capability is null
                        || this._capabilities.ContainsKey(lease.CapabilityName))
                    {
                        lease.Dispose();
                        throw new InvalidOperationException("An MCP capability contributor returned an invalid or duplicate capability name.");
                    }

                    this._capabilityLeases.Add(lease);
                    this._capabilities.Add(lease.CapabilityName, lease.Capability);
                }
            }
            catch (Exception ex)
            {
                this.ReleaseCapabilities();
                this.Logger.LogError(ex, Lang.DeviceMcpClient_Build_CreateCapabilitiesFailed, config.Session.DeviceId);
                return false;
            }

            return true;
        }

        public override async Task SendMcpInitializeAsync()
        {
            var @params = new
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = this._capabilities,
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
                Params = McpJsonHelper.ToNode(@params)
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
            JsonObject mcpMessage = new()
            {
                ["type"] = "mcp",
                ["payload"] = McpJsonHelper.ToNode(message)
            };
            string jsonMessage = mcpMessage.ToJson();
            await this.CurrentSession.SendOutter.SendAsync(jsonMessage);
        }

        protected override async Task OnMcpInitializedAsync(JsonObject result)
        {
            if (Interlocked.Exchange(ref this._initialized, 1) != 0)
            {
                return;
            }
            if (result["protocolVersion"] is not JsonValue version
                || !version.TryGetValue(out string? protocolVersion) || protocolVersion != "2024-11-05")
            {
                this.CurrentSession.PrivateProvider.FunctionToolsContext.SetMcpClientFailed(new InvalidOperationException(Lang.DeviceMcpClient_IncompatibleProtocol));
                this.Logger.LogWarning(Lang.DeviceMcpClient_IncompatibleProtocol);
                return;
            }
            try
            {
                await this.SendMcpNotificationAsync(NotificationMethods.InitializedNotification);
                await this.RequestToolsListAsync();
            }
            catch (Exception ex)
            {
                this.CurrentSession.PrivateProvider.FunctionToolsContext.SetMcpClientFailed(ex);
                this.Logger.LogWarning(ex, Lang.DeviceMcpClient_IncompatibleProtocol);
            }
        }

        public override void Dispose()
        {
            this.ReleaseCapabilities();
            this._capabilities.Clear();
        }

        private void ReleaseCapabilities()
        {
            foreach (IDeviceMcpCapabilityLease lease in this._capabilityLeases)
            {
                try
                {
                    lease.Dispose();
                }
                catch (Exception ex)
                {
                    this.Logger.LogWarning(ex, Lang.DeviceMcpClient_ReleaseCapabilities_ReleaseFailed, lease.CapabilityName);
                }
            }

            this._capabilityLeases.Clear();
        }
    }
}
