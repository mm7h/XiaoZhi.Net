using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;
using XiaoZhi.Net.Server.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class ChatAgent : BaseAgent<ChatAgent>, IChatAgent
    {
        private ChatClientAgent? _chatClientAgent;

        private AgentSession? _agentSession;

        private int _seqParagraphId = 0;
        private int _seqSentenceId = 0;

        public ChatAgent(IServiceProvider serviceProvider, ILogger<ChatAgent> logger) : base(SubAgentNames.ChatAgent, serviceProvider, logger)
        {
            
        }

        public bool UseStreaming { get; private set; }

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
                string? summaryMemory = agentBuildConfig.AgentSetting.Config.GetValueOrDefault("SummaryMemory");

                // 若有历史记忆摘要，追加到系统提示词中
                string instructions = this.Prompt;
                if (!string.IsNullOrEmpty(summaryMemory))
                {
                    instructions += "\n\n" + summaryMemory;
                }
                IChatClient chatClient = this.ServiceProvider.GetRequiredKeyedService<IChatClient>($"LLM_{agentBuildConfig.AgentSetting.ModelName}");

                bool pluginsBuildResult = this.BuildPlugins(agentBuildConfig.SessionPrivateProvider);

                ChatClientAgentOptions chatClientAgentOptions = new ChatClientAgentOptions
                {
                    Name = SubAgentNames.ChatAgent,
                    Description = $"the agent of {SubAgentNames.ChatAgent}",
                    AIContextProviders = new List<AIContextProvider>
                    {
                        new SessionFunctionToolContextProvider(agentBuildConfig.SessionPrivateProvider)
                    },
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instructions,
                        Temperature = 0.5f,
                        MaxOutputTokens = 40,
                    },
                };

                this._chatClientAgent = new ChatClientAgent(
                    chatClient: chatClient,
                    options: chatClientAgentOptions,
                    services: this.ServiceProvider
                );

                this._agentSession = this._chatClientAgent.CreateSessionAsync(agentBuildConfig.SessionPrivateProvider.Token).GetAwaiter().GetResult();

                if (pluginsBuildResult)
                {
                    //todo
                    //this.Logger.LogInformation(Lang.ChatAgent_Build_BuildPluginsBuilt, this.ProviderType, this.ModelName);
                    //this.Logger.LogInformation(Lang.ChatAgent_Build_Built, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                    return true;
                }
                else
                {
                    //todo
                    //this.Logger.LogError(Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                    //this.Logger.LogError(Lang.ChatAgent_Build_BuildPluginsFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                //todo
                //this.Logger.LogError(ex, Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                return false;
            }
        }


        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return protocolBuilder.ConfigureRoutes(routeBuilder =>
            {
                routeBuilder
                .AddHandler<string, ValueTask<string>>(this.GenerateChatResponseAsync);
            });
        }
        [MessageHandler]
        public async ValueTask<string> GenerateChatResponseAsync(string userMessage, IWorkflowContext workflowContext, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatClientAgent is null || this._agentSession is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
            }

            ChatClientAgentRunOptions runOptions = new ChatClientAgentRunOptions(new ChatOptions
            {
                ToolMode = ChatToolMode.Auto
            });
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

            StringBuilder allResponse = new StringBuilder();
            StringBuilder segmentResponse = new StringBuilder();

            ChatClientAgentRunOptions runOptions = new ChatClientAgentRunOptions(new ChatOptions
            {
                ToolMode = ChatToolMode.Auto
            });
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

        private bool BuildPlugins(PrivateProvider sessionProvider)
        {
            #region LocalMusicPlayer
            ILLMPlugin musicPlayerPlugin = this.ServiceProvider.GetRequiredService<MusicPlayer>();

            LLMPluginConfig llmPluginConfig = new LLMPluginConfig(sessionProvider);

            if (musicPlayerPlugin.Build(llmPluginConfig))
            {
                sessionProvider.FunctionTools.AddRange(musicPlayerPlugin.AsAITools());
                //todo
                //this.Logger.LogInformation(Lang.ChatAgent_Build_BuildPluginsBuilt, this.ProviderType, this.ModelName);
            }
            else
            {
                return false;
            }
            #endregion

            return true;
        }
        protected override string GenerateId()
        {
            string devicePart = this.ReplaceMacDelimiters(this.DeviceId, "_");
            string sessionPart = this.SessionId.Replace("-", string.Empty);
            if (sessionPart.Length > 7)
            {
                sessionPart = sessionPart.Substring(0, 7);
            }
            int sequence = Interlocked.Increment(ref this._seqParagraphId);
            return $"{devicePart}_{sessionPart}_{sequence}";
        }

        private string GenerateSentenceId(string paragraphId)
        {
            return $"{paragraphId}_{Interlocked.Increment(ref this._seqSentenceId)}";
        }
        public override void Dispose()
        {
        }
    }
}
