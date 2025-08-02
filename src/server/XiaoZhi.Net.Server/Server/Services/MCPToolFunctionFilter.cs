using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.MCP;

namespace XiaoZhi.Net.Server.Services
{
    internal class MCPToolFunctionFilter : IFunctionInvocationFilter
    {
        private readonly HashSet<string> _subMCPClientTypeNames = new HashSet<string>(3) { SubMCPClientTypeNames.DeviceMcpClient, SubMCPClientTypeNames.McpEndpointClient, SubMCPClientTypeNames.ServerMcpClient };
        private readonly ILogger _logger;
        public MCPToolFunctionFilter(ILogger<MCPToolFunctionFilter> logger)
        {
            this._logger = logger;
        }
        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            if (!string.IsNullOrEmpty(context.Function.PluginName) && _subMCPClientTypeNames.Contains(context.Function.PluginName) && context.Kernel.Data.TryGetValue("session", out var data) && data is not null && data is Session session)
            {
                try
                {
                    ISubMcpClient subMcpClient = session.McpClient.GetSubMcpClient(context.Function.PluginName);
                    string callResult = await subMcpClient.CallMcpToolAsync(context.Function.Name, context.Arguments);
                    context.Result = new FunctionResult(context.Result, callResult);
                }
                catch (Exception ex)
                {
                    this._logger.LogError(ex, "Failed to invoke the MCP tool function: {FunctionName} in plugin: {PluginName}.", context.Function.Name, context.Function.PluginName);
                    string failedMessage = $"Failed to invoke the MCP tool function: {context.Function.Name} in plugin: {context.Function.PluginName}, and the error message is: {ex.Message}.";
                    context.Result = new FunctionResult(context.Result, failedMessage);
                }
            }
            else
            {
                await next(context);
            }
        }
    }
}
