using Microsoft.SemanticKernel.Connectors.OpenAI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Entities;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ILlm : IProvider
    {
        event Action<string> OnBeforeTokenGenerate;
        event Action<string, OutSegment> OnTokenGenerating;
        event Action<string, string> OnTokenGenerated;
        Task ChatAsync(IEnumerable<Dialogue> dialogues, Workflow<string> workflow, OpenAIPromptExecutionSettings chatCompletionOptions, CancellationToken token);
        Task ChatByStreamingAsync(IEnumerable<Dialogue> dialogues, Workflow<string> workflow, OpenAIPromptExecutionSettings chatCompletionOptions, CancellationToken token);
    }
}
