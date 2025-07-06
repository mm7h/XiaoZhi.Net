using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ILlm : IProvider
    {
        event Action<string> OnBeforeTokenGenerate;
        event Action<string, OutSegment> OnTokenGenerating;
        event Action<string, string> OnTokenGenerated;
        Task ChatAsync(Workflow<DialogueContext> workflow, CancellationToken token);
        Task ChatByStreamingAsync(Workflow<DialogueContext> workflow, CancellationToken token);
    }
}
