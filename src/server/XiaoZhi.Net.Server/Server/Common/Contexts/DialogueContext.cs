using Microsoft.Extensions.AI;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Common.Models;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal record DialogueContext
    {
        public DialogueContext(string sessionId, IReadOnlyList<ChatMessage> chatHistory, string? llmModelName, IEnumerable<Dialogue> dialogues)
        {
            this.SessionId = sessionId;
            this.ChatHistory = chatHistory;
            this.LlmModelName = llmModelName;
            this.Dialogues = dialogues;
        }

        public string SessionId { get; }
        /// <summary>当前对话的聊天历史（用于保存记忆等扫展功能）</summary>
        public IReadOnlyList<ChatMessage> ChatHistory { get; }
        public string? LlmModelName { get; }
        public IEnumerable<Dialogue> Dialogues { get; }
    }
}
