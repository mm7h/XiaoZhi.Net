using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.Configs
{
    internal record LLMPluginConfig(PrivateProvider SessionProvider);

    internal record LLMPluginConfig<TPluginSetting>(PrivateProvider SessionProvider, TPluginSetting setting) : LLMPluginConfig(SessionProvider);
}
