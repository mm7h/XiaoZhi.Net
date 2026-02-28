using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface IEmotionAgent : IAgent
    {
        Task<Emotion> AnalyzeEmotionAsync(string userMessage, string? latestSentence, CancellationToken token);
    }
}
