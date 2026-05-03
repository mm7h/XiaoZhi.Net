using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal interface ISubMcpClient : IProvider<MCPClientBuildConfig>
    {
        /// <summary>已注册的 MCP 工具函数列表</summary>
        ICollection<AIFunction> Functions { get; }
        bool IsReady { get; }
        int NextId { get; }
        bool HasTool(string toolName);
        Task HandleMcpMessageAsync(JsonObject jsonObject);
        Task SendMcpInitializeAsync();
        Task SendMcpNotificationAsync(string method);
        Task RequestToolsListAsync();
        Task RequestToolsListAsync(string cursor);
        Task<string> CallMcpToolAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, int timeout = 30);
    }
}
