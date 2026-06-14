using Microsoft.Agents.AI.Workflows;
using System;
using XiaoZhi.Net.Server.Common.Configs;

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
        void RegisterDevice(string deviceId, string sessionId);
        void UnregisterDevice(string deviceId, string sessionId);
    }
}
