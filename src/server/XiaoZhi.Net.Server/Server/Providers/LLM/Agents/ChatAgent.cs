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

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class ChatAgent : BaseAgent<ChatAgent>
    {
        private const string FUNCTION_CALL_INTENT_TYPE = "FunctionCall";
        private const string NONE_INTENT_TYPE = "None";
        private const string ENHANCED_CHAT_PROMPT = """
请在遵守上方角色设定的前提下，额外严格遵守以下回复规则：
1. 回复要像真实语音聊天，语气自然、简短、直接，第一句先回答核心内容，不要先寒暄，不要自我解释。
2. 用户输入可能来自 ASR 转写，允许存在同音字、错别字、断句不准，你要优先理解真实意图，不要纠正用户的识别结果。
3. 除非用户明确要求切换语言，否则始终沿用当前对话语言回复。
4. 输出内容必须适合 TTS 朗读：不要使用 Markdown、代码块、XML/HTML 标签、项目符号或解释性括号动作。
5. 情绪表达必须放在每个输出句段最前面，格式固定为 [EmotionName] 正文。EmotionName 只能从以下 Emotion 枚举中选择：Neutral、Happy、Laughing、Funny、Sad、Angry、Crying、Loving、Embarrassed、Surprised、Shocked、Thinking、Winking、Cool、Relaxed、Delicious、Kissy、Confident、Sleepy、Silly、Confused。
6. 如果一个回复包含多句、分段或换行，那么每个独立句段都必须重新写一次 [EmotionName] 前缀，不能只在第一句前面标一次。
7. 情绪选择必须和正文语义一致；拿不准时统一使用 [Neutral]；需要思考、停顿、分析时优先使用 [Thinking]。
8. 表情生成规则：不要在正文中自由输出表情符号；情绪只能来源于 Emotion 枚举。如果确实需要补充可视化表情，也只能使用该 Emotion 枚举 Description 对应的单个表情，并且只能紧跟在 [EmotionName] 后面，正文其他位置禁止出现表情。
9. 不要输出 Emotion 枚举之外的情绪名称、别名、自定义标签或没有前缀的正文。

输出示例：
[Happy] 今天状态不错，我们直接开始。
[Thinking] 这个问题我先替你理一下，结论其实不复杂。
""";

        private ChatClientAgent? _chatClientAgent;

        private AgentSession? _agentSession;
        private PrivateProvider? _sessionPrivateProvider;
        private bool _allowFunctionCall;

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
                string intentType = agentBuildConfig.AgentSetting.Config.GetConfigValueOrDefault("IntentType", "None");
                this._allowFunctionCall = string.Compare(FUNCTION_CALL_INTENT_TYPE, intentType, StringComparison.OrdinalIgnoreCase) == 0;
                
                this._sessionPrivateProvider = agentBuildConfig.SessionPrivateProvider;

                string instructions = this.BuildInstructions(summaryMemory);
                IChatClient chatClient = this.ServiceProvider.GetRequiredKeyedService<IChatClient>($"LLM_{agentBuildConfig.AgentSetting.ModelName}");


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

                return true;
                //if (pluginsBuildResult)
                //{
                //    //todo
                //    //this.Logger.LogInformation(Lang.ChatAgent_Build_BuildPluginsBuilt, this.ProviderType, this.ModelName);
                //    //this.Logger.LogInformation(Lang.ChatAgent_Build_Built, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                //    return true;
                //}
                //else
                //{
                //    //todo
                //    //this.Logger.LogError(Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                //    //this.Logger.LogError(Lang.ChatAgent_Build_BuildPluginsFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                //    return false;
                //}
            }
            catch (Exception)
            {
                //todo
                //this.Logger.LogError(ex, Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                return false;
            }
        }

        private string BuildInstructions(string? summaryMemory)
        {
            StringBuilder instructionsBuilder = new StringBuilder();
            instructionsBuilder.Append(this.Prompt);
            instructionsBuilder.Append("\n\n");
            instructionsBuilder.Append(ENHANCED_CHAT_PROMPT);

            if (!string.IsNullOrWhiteSpace(summaryMemory))
            {
                instructionsBuilder.Append("\n\n");
                instructionsBuilder.Append(summaryMemory);
            }

            return instructionsBuilder.ToString();
        }


        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return protocolBuilder.ConfigureRoutes(routeBuilder =>
            {
                routeBuilder
                .AddHandler<WorkflowPreInputs>(this.GenerateChatResponseAsync)
                .AddHandler<IntentDetectionResult>(this.GenerateChatFromDetectionResultAsync);
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

        /// <summary>接收 IntentDetectionResult（未检测到意图），将用户消息转发给 LLM 正常对话</summary>
        [MessageHandler]
        public async ValueTask GenerateChatFromDetectionResultAsync(IntentDetectionResult detectionResult, IWorkflowContext workflowContext, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatClientAgent is null || this._agentSession is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
            }

            await foreach (string sentence in this.StreamLLMResponseAsync(detectionResult.UserMessage, token))
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
                ToolMode = this._allowFunctionCall ? ChatToolMode.Auto : ChatToolMode.None,
                Tools = (this._allowFunctionCall && this._sessionPrivateProvider?.FunctionTools.Count > 0)
                    ? this._sessionPrivateProvider.FunctionTools
                    : null
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

        public override void Dispose()
        {
        }
    }
}
