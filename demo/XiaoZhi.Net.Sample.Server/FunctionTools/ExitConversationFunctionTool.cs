using System.ComponentModel;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Attributes;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Sample.Server.FunctionTools
{
    /// <summary>
    /// 处理用户明确结束当前对话的请求。
    /// </summary>
    public sealed class ExitConversationFunctionTool : PrivateFunctionTool
    {
        [Description("当用户明确表达希望结束当前对话或告别时调用，例如“再见”“拜拜”“先聊到这”“结束对话”。用户是在询问退出方式、原因或状态时不要调用。")]
        [ToolBehavior(ToolAction.DirectResponse)]
        public FunctionReturn<string> HandleExitIntent([Description("用于向用户播报的简短、自然的告别语")] string sayGoodbye)
        {
            string goodbye = string.IsNullOrWhiteSpace(sayGoodbye) ? "再见，祝您生活愉快！" : sayGoodbye.Trim();
            this.SessionController.TryRequestCloseAfterChat();

            return new FunctionReturn<string>
            {
                Next = ToolAction.DirectResponse,
                Result = goodbye,
                Response = goodbye
            };
        }
    }
}
