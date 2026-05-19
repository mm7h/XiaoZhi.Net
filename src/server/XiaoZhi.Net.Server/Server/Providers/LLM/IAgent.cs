using System;
using XiaoZhi.Net.Server.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface IAgent : IDisposable
    {
        string AgentName { get; }
        string Prompt { get; }
        int Order { get; }
        bool IsEnabled { get; }
        bool SupportsStreaming { get; }
        bool Build(LLMAgentBuildConfig settings);
        void RegisterDevice(string deviceId, string sessionId);
        void UnregisterDevice(string deviceId, string sessionId);
    }
}
