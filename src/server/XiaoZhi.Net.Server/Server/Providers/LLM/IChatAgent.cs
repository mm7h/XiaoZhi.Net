using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface IChatAgent : IAgent
    {
        Task<string> GenerateChatResponseAsync(string userMessage, Emotion? emotion, CancellationToken token);
        IAsyncEnumerable<string> GenerateChatResponseStreamingAsync(string userMessage, Emotion? emotion, CancellationToken token);
    }
}
