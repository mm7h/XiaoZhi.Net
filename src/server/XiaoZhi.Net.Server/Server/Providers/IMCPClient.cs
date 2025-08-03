using System.Collections.Generic;
using XiaoZhi.Net.Server.Providers.MCP;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IMcpClient : IProvider
    {
        IDictionary<string, ISubMcpClient> GetAllSubMcpClients();
        ISubMcpClient? GetSubMcpClient(string subTypeName);
    }
}
