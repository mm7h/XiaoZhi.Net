using System.Collections.Generic;
using System.Linq;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    internal record WorkflowOutputs(bool HandledByIntent, List<ChatMessageItemResult> Results)
    {
        /// <summary>将所有段落内容拼接为完整响应文本</summary>
        public string ResponseText => string.Concat(this.Results.Select(r => r.Content));
    }
}