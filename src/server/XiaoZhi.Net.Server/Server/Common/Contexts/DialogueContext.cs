using Microsoft.SemanticKernel;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal record DialogueContext
    {
        public DialogueContext(string sessionId, Kernel kernel, string? llmModelName, IEnumerable<Dialogue> dialogues)
        {
            this.SessionId = sessionId;
            this.Kernel = kernel;
            this.LlmModelName = llmModelName;
            this.Dialogues = dialogues;
        }

        public string SessionId { get; }
        public Kernel Kernel { get; }
        public string? LlmModelName { get; }
        public IEnumerable<Dialogue> Dialogues { get; }
    }
}
