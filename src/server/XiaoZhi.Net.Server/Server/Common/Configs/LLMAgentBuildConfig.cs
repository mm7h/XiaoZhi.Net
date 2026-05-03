using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Server.Common.Configs
{
    internal record LLMAgentBuildConfig(ModelSetting AgentSetting, PrivateProvider SessionPrivateProvider);
}
