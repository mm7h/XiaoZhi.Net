using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.LLM.Utils;

namespace XiaoZhi.Net.Server.Common.Configs
{
    internal record LLMAgentBuildConfig(
        ModelSetting AgentSetting,
        PrivateProvider SessionPrivateProvider,
        ChatHistorySequence ChatHistorySequence);
}
