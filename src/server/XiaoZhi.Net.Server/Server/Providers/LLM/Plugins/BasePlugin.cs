using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Plugins
{
    internal abstract class BasePlugin<TLogger> : BaseProvider<TLogger, LLMPluginConfig>, ILLMPlugin
    {
        protected BasePlugin(ILogger<TLogger> logger) : base(logger)
        {
            
        }

        protected Session CurrentSession { get; set; } = null!;

        public override string ProviderType => "llm plugin";
    }
}
