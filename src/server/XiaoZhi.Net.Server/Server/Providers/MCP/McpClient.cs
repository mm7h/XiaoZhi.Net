using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.MCP.DeviceMcp;
using XiaoZhi.Net.Server.Providers.MCP.McpEndpoint;
using XiaoZhi.Net.Server.Providers.MCP.ServerMcp;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal class McpClient : BaseProvider, IMcpClient
    {
        private readonly Dictionary<string, ISubMcpClient> _subMcpClients = new Dictionary<string, ISubMcpClient>();

        private readonly Session _currentSession;
        private readonly XiaoZhiConfig _config;

        public override string ProviderType => "McpClient";

        public McpClient(Session session, XiaoZhiConfig config, ILogger logger) : base(logger)
        {
            this._currentSession = session;
            this._config = config;
        }

        public IDictionary<string, ISubMcpClient> GetAllSubMcpClients() => this._subMcpClients;

        public ISubMcpClient GetSubMcpClient(string subTypeName)
        {
            if (this._subMcpClients.ContainsKey(subTypeName))
            {
                return this._subMcpClients[subTypeName];
            }
            else
            {
                throw new KeyNotFoundException($"SubMcpClient with type name '{subTypeName}' not found.");
            }
        }

        public override bool Build()
        {
            if (this._config.McpSettings is null)
            {
                return true;
            }
            ModelSetting defaultSetting = new ModelSetting();

            if (this._config.McpSettings.TryGetValue(SubMCPClientTypeNames.DeviceMcpClient, out var deviceSetting))
            {
                ISubMcpClient deviceMcpClient = new DeviceMcpClient(this._currentSession, deviceSetting, this.Logger);
                this._subMcpClients.Add(SubMCPClientTypeNames.DeviceMcpClient, deviceMcpClient);
            }
            else
            {
                ModelSetting defaultDeviceMcpSetting = new ModelSetting
                {
                    ModelName = SubMCPClientTypeNames.DeviceMcpClient
                };
                ISubMcpClient deviceMcpClient = new DeviceMcpClient(this._currentSession, defaultDeviceMcpSetting, this.Logger);
                this._subMcpClients.Add(SubMCPClientTypeNames.DeviceMcpClient, deviceMcpClient);
            }

            if (this._config.McpSettings.TryGetValue(SubMCPClientTypeNames.McpEndpointClient, out var endPointSetting))
            {
                ISubMcpClient mcpEndpointClient = new McpEndpointClient(this._currentSession, endPointSetting, this.Logger);
                this._subMcpClients.Add(SubMCPClientTypeNames.McpEndpointClient, mcpEndpointClient);
            }

            if (this._config.McpSettings.TryGetValue(SubMCPClientTypeNames.ServerMcpClient, out var serverMCPSetting))
            {
                ISubMcpClient serverMcpClient = new ServerMcpClient(this._currentSession, serverMCPSetting, this.Logger);
                this._subMcpClients.Add(SubMCPClientTypeNames.ServerMcpClient, serverMcpClient);
            }

            return true;
            if (this._subMcpClients.Any())
            {
                //var buildResults = this._subMcpClients.Values
                //.AsParallel()
                //.Select(client => client.Build())
                //.ToArray();

                //return buildResults.All(result => result);

                foreach (var subMcpClient in this._subMcpClients.Values)
                {
                    if (!subMcpClient.Build())
                    {
                        this.Logger.LogError("Failed to build the MCP client: {clientType}.", subMcpClient.ProviderType);
                        return false;
                    }
                }

                return true;
            }
            else
            {
                this.Logger.LogWarning("No mcp client builed for the Session {sessionId}.");
                return false;
            }
        }

        public override void Dispose()
        {
            foreach (var subMcpClient in this._subMcpClients.Values)
            {
                subMcpClient.Dispose();
            }

            this._subMcpClients.Clear();
        }
    }
}
