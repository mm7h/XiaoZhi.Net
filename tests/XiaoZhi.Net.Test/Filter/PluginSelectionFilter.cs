using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace XiaoZhi.Net.Test.Filter
{
    public class PluginSelectionFilter : IFunctionInvocationFilter
    {
        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            foreach (var argument in context.Arguments)
            {
                Console.WriteLine($"arg key: {argument.Key}, arg val: {argument.Value}");
            }
            //context.Result = new FunctionResult(context.Result, "function调用失败，无法打开网站");

            //return;
            if (context.Function.Metadata.AdditionalProperties.TryGetValue("sessionId", out var sessionId) && !string.IsNullOrEmpty(sessionId?.ToString()))
            {
                Console.WriteLine($"当前获取到SessionId: {sessionId}，理论上不继续执行Function");
            }
            else
            {
                Console.WriteLine($"调用了function: {context.Function.Name}");
                await next(context);
            }
        }
    }
}
