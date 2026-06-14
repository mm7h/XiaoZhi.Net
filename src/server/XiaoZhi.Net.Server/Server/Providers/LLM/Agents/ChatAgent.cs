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

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class ChatAgent : BaseAgent<ChatAgent>, IChatAgent
    {
        private ChatClientAgent? _chatClientAgent;

        private AgentSession? _agentSession;

        public ChatAgent(IServiceProvider serviceProvider, ILogger<ChatAgent> logger) : base(SubAgentNames.ChatAgent, serviceProvider, logger)
        {

        }

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
                string? summaryMemory = agentBuildConfig.AgentSetting.Config.GetValueOrDefault("SummaryMemory");

                // 若有历史记忆摘要，追加到系统提示词中
                string instructions = this.Prompt;
                if (!string.IsNullOrEmpty(summaryMemory))
                {
                    instructions += "\n\n" + summaryMemory;
                }
                IChatClient chatClient = this.ServiceProvider.GetRequiredKeyedService<IChatClient>($"LLM_{agentBuildConfig.AgentSetting.ModelName}");

                //todo
                bool pluginsBuildResult = this.BuildPlugins(agentBuildConfig.SessionPrivateProvider);

                ChatClientAgentOptions chatClientAgentOptions = new ChatClientAgentOptions
                {
                    Name = SubAgentNames.ChatAgent,
                    Description = $"the agent of {SubAgentNames.ChatAgent}",
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instructions,
                        Temperature = 0.5f,
                        MaxOutputTokens = 40,
                        ResponseFormat = ChatResponseFormat.Text
                    }
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
                .AddHandler<WorkflowPreInputs>(this.GenerateChatResponseAsync)
                .AddHandler<IntentResult>(this.GenerateChatFromIntentResponseAsync);
            })
            .SendsMessage<string>();
        }

        
        [MessageHandler]
        public async ValueTask GenerateChatResponseAsync(WorkflowPreInputs preInput, IWorkflowContext workflowContext, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatClientAgent is null || this._agentSession is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
            }

            await foreach (string sentence in this.StreamLLMResponseAsync(preInput.UserMessage, token))
            {
                await workflowContext.SendMessageAsync(sentence, token);
            }
        }

        /// <summary>接收IntentResult，逐句将原始文本（含Emotion标识前缀）发送给OutputAgent</summary>
        [MessageHandler]
        public async ValueTask GenerateChatFromIntentResponseAsync(IntentResult intentResult, IWorkflowContext workflowContext, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatClientAgent is null || this._agentSession is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
            }

            // 逐句发送原始文本（含 [Emotion] 前缀）给 OutputAgent 统一处理
            await foreach (string sentence in this.StreamLLMResponseAsync(intentResult.UserMessage, token))
            {
                await workflowContext.SendMessageAsync(sentence, token);
            }
        }

        /// <summary>流式调用LLM，按标点符号分句逐句返回</summary>
        private async IAsyncEnumerable<string> StreamLLMResponseAsync(string userMessage, [EnumeratorCancellation] CancellationToken token)
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
        public override void Dispose()
        {
        }
    }
}
