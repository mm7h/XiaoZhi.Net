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
        /// <summary>当前对话的聊天历史，用于保存记忆等功能</summary>
        IReadOnlyList<ChatMessage> LLMChatHistory { get; }
        Task StartDialogueAsync(string userMessage, CancellationToken token);
    }
}
