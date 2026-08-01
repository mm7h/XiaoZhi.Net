using System;
using System.Collections.Generic;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface IAgent : IIdentified, IDisposable
    {
        string AgentName { get; }
        string Prompt { get; }
        int Order { get; }
        bool IsEnabled { get; }
        bool SupportsStreaming { get; }
        bool Build(LLMAgentBuildConfig settings);
        Executor AsExecutor();
        IReadOnlyList<AgentChatHistoryItem> GetChatHistory();
        void RegisterDevice(string deviceId, string sessionId);
        void UnregisterDevice(string deviceId, string sessionId);
    }
}
