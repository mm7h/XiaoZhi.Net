using Microsoft.SemanticKernel;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal interface ISubMcpClient : IProvider
    {
        ICollection<KernelFunction> Functions { get; }
        bool IsReady { get; }
        int NextId { get; }
        bool HasTool(string toolName);
        Task HandleMcpMessageAsync(JsonObject jsonObject);
        Task SendMcpInitializeAsync();
        Task SendMcpNotificationAsync(string method);
        Task RequestToolsListAsync();
        Task RequestToolsListAsync(string cursor);
        Task<string> CallMcpToolAsync(string toolName, KernelArguments arguments, int timeout = 30);
    }
}
