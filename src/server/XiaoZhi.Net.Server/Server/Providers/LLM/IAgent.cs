using XiaoZhi.Net.Server.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface IAgent : IProvider<LLMAgentBuildConfig>
    {
        string Prompt { get; }
        int Order { get; }
        bool IsEnabled { get; }
        bool SupportsStreaming { get; }
    }
}
