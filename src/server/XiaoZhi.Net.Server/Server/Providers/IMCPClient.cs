using System.Collections.Generic;
using XiaoZhi.Net.Server.Providers.MCP;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IMcpClient : IProvider<Dictionary<string, MCPClientBuildConfig>>
    {
        IDictionary<string, ISubMcpClient> GetAllSubMcpClients();
        ISubMcpClient? GetSubMcpClient(string subTypeName);
    }
}
