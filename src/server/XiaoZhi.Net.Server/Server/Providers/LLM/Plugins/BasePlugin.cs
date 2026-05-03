using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM.Plugins
{
    internal abstract class BasePlugin<TLogger> : BaseProvider<TLogger, LLMPluginConfig>, ILLMPlugin
    {
        protected BasePlugin(ILogger<TLogger> logger) : base(logger)
        {
            
        }

        protected PrivateProvider CurrentSessionProvider { get; set; } = null!;

        public override string ProviderType => "llm plugin";

        public abstract IEnumerable<AITool> AsAITools();
    }
}
