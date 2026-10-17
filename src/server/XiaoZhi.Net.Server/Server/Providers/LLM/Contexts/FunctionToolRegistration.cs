using Microsoft.Extensions.AI;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    /// <summary>
    /// 自定义函数工具在服务端内部的注册信息。
    /// </summary>
    internal sealed class FunctionToolRegistration
    {
        public FunctionToolRegistration(FunctionMetadata metadata, ToolAction toolAction)
        {
            this.Metadata = metadata;
            this.DefaultAction = toolAction;
        }
        public FunctionToolRegistration(AIFunction function, ToolAction toolAction)
        {
            this.Function = function;
            this.Metadata = FunctionToolHelper.ToFunctionMetadata(function);
            this.DefaultAction = toolAction;
        }

        public FunctionToolRegistration(AIFunction function, FunctionMetadata metadata, ToolAction toolAction)
        {
            this.Function = function;
            this.Metadata = metadata;
            this.DefaultAction = toolAction;
        }


        public AIFunction Function { get; private set; } = null!;

        public FunctionMetadata Metadata { get; }

        public ToolAction DefaultAction { get; }

        public void WithFunction(AIFunction function)
        {
            this.Function = function;
        }
    }
}
