using Microsoft.Extensions.AI;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    /// <summary>
    /// 自定义函数工具在服务端内部的注册信息。
    /// </summary>
    internal sealed class FunctionToolRegistration
    {
        public FunctionToolRegistration(AIFunction function, FunctionMetadata metadata, ToolAction defaultAction)
        {
            this.Function = function;
            this.Metadata = metadata;
            this.DefaultAction = defaultAction;
        }

        public AIFunction Function { get; }

        public FunctionMetadata Metadata { get; }

        public ToolAction DefaultAction { get; }
    }
}