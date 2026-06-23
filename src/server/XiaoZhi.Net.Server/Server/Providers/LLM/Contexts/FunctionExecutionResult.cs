using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    internal sealed record FunctionExecutionResult(
        string FunctionName,
        string? Response,
        ToolAction Action,
        string UserMessage);
}
