using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal class McpClient : BaseProvider<McpClient, Dictionary<string, MCPClientBuildConfig>>, IMcpClient
    {
        private readonly Dictionary<string, ISubMcpClient> _subMcpClients = new Dictionary<string, ISubMcpClient>();

        private readonly IServiceProvider _serviceProvider;

        public override string ModelName => nameof(McpClient);
        public override string ProviderType => "McpClient";

        public McpClient(IServiceProvider serviceProvider, ILogger<McpClient> logger) : base(logger)
        {
            this._serviceProvider = serviceProvider;
        }

        public IDictionary<string, ISubMcpClient> GetAllSubMcpClients() => this._subMcpClients;

        public ISubMcpClient? GetSubMcpClient(string subTypeName)
        {
            if (this._subMcpClients.TryGetValue(subTypeName, out var subMcpClient))
            {
                return subMcpClient;
            }
            else
            {
                this.Logger.LogError("SubMcpClient with type name '{subTypeName}' not found.", subTypeName);
                return null;
            }
        }

        public override bool Build(Dictionary<string, MCPClientBuildConfig> mcpSettings)
        {
            // DeviceMcpClient
            ISubMcpClient deviceMcpClient = this._serviceProvider.GetRequiredKeyedService<ISubMcpClient>(SubMCPClientTypeNames.DeviceMcpClient);
            this._subMcpClients.Add(SubMCPClientTypeNames.DeviceMcpClient, deviceMcpClient);

            // McpEndpointClient
            if (mcpSettings.ContainsKey(SubMCPClientTypeNames.McpEndpointClient))
            {
                ISubMcpClient mcpEndpointClient = this._serviceProvider.GetRequiredKeyedService<ISubMcpClient>(SubMCPClientTypeNames.McpEndpointClient);
                this._subMcpClients.Add(SubMCPClientTypeNames.McpEndpointClient, deviceMcpClient);
            }

            // ServerMcpClient
            if (mcpSettings.ContainsKey(SubMCPClientTypeNames.ServerMcpClient))
            {
                ISubMcpClient serverMcpClient = this._serviceProvider.GetRequiredKeyedService<ISubMcpClient>(SubMCPClientTypeNames.ServerMcpClient);
                this._subMcpClients.Add(SubMCPClientTypeNames.ServerMcpClient, serverMcpClient);
            }

            var buildResults = this._subMcpClients.Values
                .AsParallel()
                .Select(client => client.Build(mcpSettings[client.ModelName]))
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
