using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    internal record WorkflowOutputs(bool HandledByIntent, ChatMessageResult Results);
}