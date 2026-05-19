using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface IIntentAgent : IAgent
    {
        Task<IntentResult> DetectIntentAsync(string userMessage, CancellationToken token);
    }
}