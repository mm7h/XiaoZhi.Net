using System.Collections.Generic;
using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class DialogueContext
    {
        public DialogueContext(string sessionId, string? llmModelName, IEnumerable<Dialogue> dialogues)
        {
            this.SessionId = sessionId;
            this.LlmModelName = llmModelName;
            this.Dialogues = dialogues;
        }

        public string SessionId { get; }
        public string? LlmModelName { get; }
        public IEnumerable<Dialogue> Dialogues { get; }
    }
}
