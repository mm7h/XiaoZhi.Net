using ModelContextProtocol.Protocol;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal interface ISubMcpClient
    {
        ICollection<Tool> Tools { get; }
        bool IsReady { get; }
        int NextId { get; }
        //Task HandleMcpMessageAsync(JsonObject jsonObject);
        //Task SendMcpInitializeAsync(string clientName);
        //Task SendMcpNotificationAsync(string method);
    }
}
