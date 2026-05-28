using Microsoft.Extensions.AI;
using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface IChatAgent : IAgent
    {
        /// <summary>当前对话历史</summary>
        List<ChatMessage> ChatHistory { get; }
    }
}
