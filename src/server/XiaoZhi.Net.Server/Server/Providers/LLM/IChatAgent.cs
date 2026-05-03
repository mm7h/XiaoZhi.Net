using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface IChatAgent : IAgent
    {
        bool UseStreaming { get; }
        /// <summary>当前对话历史</summary>
        List<ChatMessage> ChatHistory { get; }
        Task<string> GenerateChatResponseAsync(string userMessage, CancellationToken token);
        IAsyncEnumerable<string> GenerateChatResponseStreamingAsync(string userMessage, CancellationToken token);
    }
}
