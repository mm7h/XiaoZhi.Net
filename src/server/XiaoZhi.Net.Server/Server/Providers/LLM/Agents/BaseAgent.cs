using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

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

        public virtual IReadOnlyList<AgentChatHistoryItem> GetChatHistory()
        {
            return [];
        }

        public virtual void RegisterDevice(string deviceId, string sessionId)
        {
            this.DeviceId = deviceId;
            this.SessionId = sessionId;
            //todo
            //this.Logger.LogInformation(Lang.BaseProvider_RegisterDevice_Registered, this.DeviceId, this.SessionId, this.ProviderType);
        }

        public virtual void UnregisterDevice(string deviceId, string sessionId)
        {
            //todo
            //this.Logger.LogInformation(Lang.BaseProvider_UnregisterDevice_Unregistered, this.DeviceId, this.SessionId, this.ProviderType);
            this.DeviceId = string.Empty;
            this.SessionId = string.Empty;
        }

        public virtual bool CheckDeviceRegistered(string deviceId, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(this.DeviceId) || string.IsNullOrWhiteSpace(this.SessionId))
            {
                //todo
                //this.Logger.LogError(Lang.BaseProvider_CheckDeviceRegistered_NotRegistered, string.IsNullOrWhiteSpace(this.DeviceId) ? "unkonwn" : this.DeviceId, string.IsNullOrWhiteSpace(this.SessionId) ? "unkonwn" : this.SessionId, this.ProviderType);
                return false;
            }
            return true;
        }

        protected virtual string GenerateId()
        {
            return Guid.NewGuid().ToString("N");
        }

        protected string ReplaceMacDelimiters(string deviceId, string newDelimiter = "")
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                //todo
                throw new ArgumentException("", nameof(deviceId));
            }

            return Regex.Replace(deviceId, @"[^a-fA-F0-9]", newDelimiter);
        }
    }
}
