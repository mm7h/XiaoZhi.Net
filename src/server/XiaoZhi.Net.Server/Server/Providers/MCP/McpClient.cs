using Microsoft.Extensions.Logging;
using System;
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
            ModelSetting defaultSetting = new ModelSetting();

            ISubMcpClient deviceMcpClient = new DeviceMcpClient(this._currentSession, this._config.McpSettings is not null && this._config.McpSettings.TryGetValue(SubMCPClientTypeNames.DeviceMcpClient, out var deviceSetting) ? deviceSetting : defaultSetting, this.Logger);
            ISubMcpClient mcpEndpointClient = new McpEndpointClient(this._currentSession, this._config.McpSettings is not null && this._config.McpSettings.TryGetValue(SubMCPClientTypeNames.McpEndpointClient, out var endPointSetting) ? endPointSetting : defaultSetting, this.Logger);
            ISubMcpClient serverMcpClient = new ServerMcpClient(this._currentSession, this._config.McpSettings is not null && this._config.McpSettings.TryGetValue(SubMCPClientTypeNames.ServerMcpClient, out var serverMCPSetting) ? serverMCPSetting : defaultSetting, this.Logger);

            this._subMcpClients.Add(SubMCPClientTypeNames.DeviceMcpClient, deviceMcpClient);
            this._subMcpClients.Add(SubMCPClientTypeNames.McpEndpointClient, mcpEndpointClient);
            this._subMcpClients.Add(SubMCPClientTypeNames.ServerMcpClient, serverMcpClient);

            var buildResults = this._subMcpClients.Values
                .AsParallel()
                .Select(client => client.Build())
                .ToArray();

            return buildResults.All(result => result);
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
