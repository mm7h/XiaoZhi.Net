using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.MCP;

namespace XiaoZhi.Net.Server.Services
{
    internal class MCPToolFunctionFilter : IFunctionInvocationFilter
    {

        public MCPToolFunctionFilter()
        {

        }
        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            if (!string.IsNullOrEmpty(context.Function.PluginName) && context.Kernel.Data.TryGetValue("session", out var data) && data is not null && data is Session session)
            {
                try
                {
                    ISubMcpClient subMcpClient = session.McpClient.GetSubMcpClient(context.Function.PluginName);
                    string callResult = await subMcpClient.CallMcpToolAsync(context.Function.Name, context.Arguments);

                    context.Result = new FunctionResult(context.Result, callResult);
                }
                catch (Exception ex)
                {
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
