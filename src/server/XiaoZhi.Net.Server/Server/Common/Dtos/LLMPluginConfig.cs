using Microsoft.SemanticKernel;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal record LLMPluginConfig(Kernel Kernel);

    internal record LLMPluginConfig<TPluginSetting> : LLMPluginConfig
    {
        public LLMPluginConfig(Kernel kernel, TPluginSetting setting) : base(kernel)
        {
            this.Setting = setting;
        }

        public TPluginSetting Setting { get; }
    }
}
