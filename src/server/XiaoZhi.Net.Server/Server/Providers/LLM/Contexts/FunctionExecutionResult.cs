using XiaoZhi.Net.Server.Abstractions;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    internal sealed record FunctionExecutionResult(
        string FunctionName,
        string? Response,
        ToolAction Action,
        string UserMessage);
}
