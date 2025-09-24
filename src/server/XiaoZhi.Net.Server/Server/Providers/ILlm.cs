using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ILlm : IProvider<LLMBuildConfig>
    {
        event Action OnBeforeTokenGenerate;
        event Action<OutSegment> OnTokenGenerating;
        event Action<string> OnTokenGenerated;
        string LLMModelName { get; }
        bool UseStreaming { get; }
        ChatHistory LLMChatHistory { get; }
        Task ChatAsync(string userMessage, CancellationToken token);
        Task ChatByStreamingAsync(string userMessage, CancellationToken token);
    }
}
