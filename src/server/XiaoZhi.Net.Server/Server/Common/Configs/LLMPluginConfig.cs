using Microsoft.SemanticKernel;

namespace XiaoZhi.Net.Server.Common.Configs
{
    internal record LLMPluginConfig(Kernel Kernel);

    internal record LLMPluginConfig<TPluginSetting>(Kernel Kernel, TPluginSetting setting) : LLMPluginConfig(Kernel);
}
