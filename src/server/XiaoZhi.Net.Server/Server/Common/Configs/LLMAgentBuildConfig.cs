using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.LLM.AIContextProviders;

namespace XiaoZhi.Net.Server.Common.Configs
{
    internal record LLMAgentBuildConfig(
        ModelSetting AgentSetting,
        PrivateProvider SessionPrivateProvider,
        SessionChatHistoryProvider ChatHistoryProvider,
        string? MemoryInstruction);
}
