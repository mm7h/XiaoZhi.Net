using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ILlm : IProvider<ModelSetting>
    {
        event Action OnBeforeTokenGenerate;
        event Action<OutSegment> OnTokenGenerating;
        event Action<string> OnTokenGenerated;
        Task ChatAsync(DialogueContext dialogueContext, CancellationToken token);
        Task ChatByStreamingAsync(DialogueContext dialogueContext, CancellationToken token);
    }
}
