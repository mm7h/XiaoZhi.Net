using Microsoft.Extensions.AI;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    /// <summary>
    /// A chat message emitted by a sub-agent together with its session-wide order.
    /// </summary>
    internal sealed record AgentChatHistoryItem(long Sequence, ChatMessage Message);
}
