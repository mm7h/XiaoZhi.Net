using XiaoZhi.Net.Server.Providers.MCP;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IMcpClient : IProvider
    {
        ISubMcpClient GetSubMcpClient(string subTypeName);
    }
}
