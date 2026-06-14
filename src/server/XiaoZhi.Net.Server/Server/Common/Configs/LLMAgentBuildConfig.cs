using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.Configs
{
    internal record LLMAgentBuildConfig(ModelSetting AgentSetting, PrivateProvider SessionPrivateProvider);
}
