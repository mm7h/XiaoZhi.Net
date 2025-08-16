using System.Collections.Generic;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Providers.MCP;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IMcpClient : IProvider<Dictionary<string, MCPClientBuildConfig>>
    {
        IDictionary<string, ISubMcpClient> GetAllSubMcpClients();
        ISubMcpClient? GetSubMcpClient(string subTypeName);
    }
}
