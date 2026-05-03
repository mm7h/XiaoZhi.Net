using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;
using XiaoZhi.Net.Server.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class ChatAgent : BaseAgent<ChatAgent>, IChatAgent
    {
        private ChatClientAgent? _chatClientAgent;

        private AgentSession? _agentSession;
        /// <summary>共享工具列表引用，IoT/MCP 会动态向其中注册工具</summary>
        private IList<AITool>? _sharedTools;

        public ChatAgent(IServiceProvider serviceProvider, ILogger<ChatAgent> logger) : base(serviceProvider, logger)
        {
        }

        public bool UseStreaming { get; private set; }

        public override string ModelName => SubAgentNames.ChatAgent;
        public override int Order => 10;

        public List<ChatMessage> ChatHistory
        {
            get
            {
                if (this._agentSession is null) return new List<ChatMessage>();
                this._agentSession.TryGetInMemoryChatHistory(out List<ChatMessage>? history, jsonSerializerOptions: JsonHelper.OPTIONS);
                return history ?? new List<ChatMessage>();
            }
        }

        public override bool Build(LLMAgentBuildConfig agentBuildConfig)
        {
            try
            {
                this.Prompt = agentBuildConfig.AgentSetting.Config.GetConfigValueOrDefault("Prompt")!;
                this.UseStreaming = agentBuildConfig.AgentSetting.Config.GetConfigValueOrDefault("UseStreaming", false);
                string ? summaryMemory = agentBuildConfig.AgentSetting.Config.GetValueOrDefault("SummaryMemory");

                // 若有历史记忆摘要，追加到系统提示词中
                string instructions = this.Prompt;
                if (!string.IsNullOrEmpty(summaryMemory))
                {
                    instructions += "\n\n" + summaryMemory;
                }
                IChatClient chatClient = this.ServiceProvider.GetRequiredKeyedService<IChatClient>($"LLM_{agentBuildConfig.AgentSetting.ModelName}");

                this._chatClientAgent = new ChatClientAgent(
                    chatClient: chatClient,
                    instructions: instructions,
                    name: nameof(ChatAgent),
                    description: $"the agent of {nameof(ChatAgent)}",
                    services: this.ServiceProvider
                );

                // 创建 AgentSession，对话历史将存储于其 StateBag
                this._agentSession = this._chatClientAgent.CreateSessionAsync(agentBuildConfig.SessionPrivateProvider.Token).GetAwaiter().GetResult();

                bool pluginsBuildResult = this.BuildPlugins(agentBuildConfig.SessionPrivateProvider);

                if (pluginsBuildResult)
                {
                    this.Logger.LogInformation(Lang.ChatAgent_Build_BuildPluginsBuilt, this.ProviderType, this.ModelName);
                    this.Logger.LogInformation(Lang.ChatAgent_Build_Built, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                    return true;
                }
                else
                {
                    this.Logger.LogError(Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                    this.Logger.LogError(Lang.ChatAgent_Build_BuildPluginsFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                return false;
            }
        }

        public override void RegisterDevice(string deviceId, string sessionId)
        {
            base.RegisterDevice(deviceId, sessionId);
        }

        public async Task<string> GenerateChatResponseAsync(string userMessage, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatClientAgent is null || this._agentSession is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
            }

            ChatClientAgentRunOptions runOptions = this.BuildCurrentRunOptions();
            AgentResponse response = await this._chatClientAgent.RunAsync(userMessage, this._agentSession, runOptions, token);

            string content = response.Text ?? string.Empty;
            string assistantContent = MarkdownCleaner.CleanMarkdown(
                Regex.Replace(Regex.Unescape(content), @"<think>.*?</think>", string.Empty, RegexOptions.Singleline));

            return assistantContent;
        }

        public async IAsyncEnumerable<string> GenerateChatResponseStreamingAsync(string userMessage, [EnumeratorCancellation] CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatClientAgent is null || this._agentSession is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
            }


            ChatClientAgentRunOptions runOptions = this.BuildCurrentRunOptions();
            StringBuilder allResponse = new StringBuilder();
            StringBuilder segmentResponse = new StringBuilder();

            await foreach (AgentResponseUpdate update in this._chatClientAgent.RunStreamingAsync(userMessage, this._agentSession, runOptions, token))
            {
                string content = update.Text ?? string.Empty;
                string text = MarkdownCleaner.CleanMarkdown(Regex.Unescape(content));
                segmentResponse.Append(text);
                string currentSegment = segmentResponse.ToString();
                Match match = DialogueHelper.SENTENCE_SPLIT_REGEX.Match(currentSegment);
                while (match.Success)
                {
                    int splitPosition = match.Index + match.Length;
                    string sentence = currentSegment.Substring(0, splitPosition);

                    allResponse.Append(sentence);
                    yield return sentence;

                    string remaining = currentSegment.Substring(splitPosition);
                    segmentResponse.Clear();
                    segmentResponse.Append(remaining);
                    currentSegment = remaining;
                    match = DialogueHelper.SENTENCE_SPLIT_REGEX.Match(currentSegment);
                }
            }

            // 处理 LLM 回复内容无法被句子分隔的情况
            if (segmentResponse.Length > 0)
            {
                string sentence = segmentResponse.ToString();
                allResponse.Append(sentence);
                yield return sentence;
            }
        }

        /// <summary>构建本次调用的运行选项，动态注入当前共享工具列表</summary>
        private ChatClientAgentRunOptions BuildCurrentRunOptions()
        {
            var chatOptions = new ChatOptions
            {
                Temperature = 0.5f,
                MaxOutputTokens = 40
            };
            if (this._sharedTools != null && this._sharedTools.Count > 0)
            {
                chatOptions.Tools = new List<AITool>(this._sharedTools);
                chatOptions.ToolMode = ChatToolMode.Auto;
            }
            return new ChatClientAgentRunOptions(chatOptions);
        }

        private bool BuildPlugins(PrivateProvider sessionProvider)
        {
            #region LocalMusicPlayer
            ILLMPlugin musicPlayerPlugin = this.ServiceProvider.GetRequiredService<MusicPlayer>();

            LLMPluginConfig llmPluginConfig = new LLMPluginConfig(sessionProvider);

            if (musicPlayerPlugin.Build(llmPluginConfig))
            {
                sessionProvider.FunctionTools.AddRange(musicPlayerPlugin.AsAITools());
                this.Logger.LogInformation(Lang.ChatAgent_Build_BuildPluginsBuilt, this.ProviderType, this.ModelName);
            }
            else
            {
                return false;
            }
            #endregion

            return true;
        }

        public override void Dispose()
        {
        }
    }
}
