using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ILlm : IProvider<LLMBuildConfig>
    {
        event Action OnBeforeTokenGenerate;
        event Action<OutSegment> OnTokenGenerating;
        event Action<IEnumerable<OutSegment>> OnTokenGenerated;

        IReadOnlyList<ChatMessage> GetChatHistory();
        Task StartDialogueAsync(string userMessage, CancellationToken token);
    }
}
