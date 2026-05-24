using System.Collections.Generic;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    internal record ChatMessageResult(List<ChatMessageItemResult> ChatMessageItems);

    internal record ChatMessageItemResult(Emotion Emotion, string Content);
}
