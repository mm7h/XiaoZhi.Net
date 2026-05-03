using Microsoft.Extensions.AI;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal interface ILLMPlugin : IProvider<LLMPluginConfig>
    {
        IEnumerable<AITool> AsAITools();
    }
    internal interface ILLMPlugin<TPluginSettings> : IProvider<LLMPluginConfig<TPluginSettings>>
    {
        IEnumerable<AITool> AsAITools();
    }
}
