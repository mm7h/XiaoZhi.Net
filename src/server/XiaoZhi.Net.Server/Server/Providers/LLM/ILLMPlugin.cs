using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface ILLMPlugin : IProvider<LLMPluginConfig>
    {
    }
    internal interface ILLMPlugin<TPluginSettings> : IProvider<LLMPluginConfig<TPluginSettings>>
    {
    }
}
