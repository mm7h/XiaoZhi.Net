using Microsoft.Extensions.Logging;
using System;
using XiaoZhi.Net.Server.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal abstract class BaseAgent<TLogger> : BaseProvider<TLogger, LLMAgentBuildConfig>, IAgent
    {
        protected BaseAgent(IServiceProvider serviceProvider, ILogger<TLogger> logger) : base(logger)
        {
            this.ServiceProvider = serviceProvider;
        }
        public IServiceProvider ServiceProvider { get; set; }
        public override string ProviderType => "llm agent";
        public string Prompt { get; protected set; } = string.Empty;
        public abstract int Order { get; }
        public virtual bool IsEnabled { get; protected set; } = true;
        public virtual bool SupportsStreaming { get; protected set; } = true;
    }
}
