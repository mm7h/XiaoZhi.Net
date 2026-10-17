using System;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal abstract class BaseAgent<TLogger> : Executor, IAgent
    {
        protected BaseAgent(string agentName, IServiceProvider serviceProvider, ILogger<TLogger> logger) : base(agentName, declareCrossRunShareable: true)
        {
            this.ServiceProvider = serviceProvider;
            this.Logger = logger;
            this.AgentName = agentName;
        }
        public IServiceProvider ServiceProvider { get; set; }
        public string AgentName { get; }
        public string Prompt { get; protected set; } = "You are a helpful assistant.";
        public abstract int Order { get; }
        public virtual bool IsEnabled { get; protected set; } = true;
        public virtual bool SupportsStreaming { get; protected set; } = true;
        protected ILogger<TLogger> Logger { get; }
        protected string SessionId { get; set; } = string.Empty;
        protected string DeviceId { get; set; } = string.Empty;
        public abstract bool Build(LLMAgentBuildConfig buildConfig);
        public abstract void Dispose();

        public Executor AsExecutor() => this;

        public virtual void RegisterDevice(string deviceId, string sessionId)
        {
            this.DeviceId = deviceId;
            this.SessionId = sessionId;
            this.Logger.LogInformation(Lang.BaseAgent_RegisterDevice_Registered, this.AgentName, this.DeviceId, this.SessionId);
        }

        public virtual void UnregisterDevice(string deviceId, string sessionId)
        {
            this.Logger.LogInformation(Lang.BaseAgent_UnregisterDevice_Unregistered, this.AgentName, this.DeviceId, this.SessionId);
            this.DeviceId = string.Empty;
            this.SessionId = string.Empty;
        }

        public virtual bool CheckDeviceRegistered(string deviceId, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(this.DeviceId) || string.IsNullOrWhiteSpace(this.SessionId))
            {
                this.Logger.LogError(
                    Lang.BaseAgent_CheckDeviceRegistered_NotRegistered,
                    this.AgentName,
                    string.IsNullOrWhiteSpace(deviceId) ? "unknown" : deviceId,
                    string.IsNullOrWhiteSpace(sessionId) ? "unknown" : sessionId);
                return false;
            }
            return true;
        }
    }
}
