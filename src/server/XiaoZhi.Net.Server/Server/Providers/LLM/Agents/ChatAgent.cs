using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM.AIContextProviders;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Providers.LLM.Utils;
using XiaoZhi.Net.Server.Resources;

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

        private readonly object _chatHistoryLock = new object();
        private readonly List<AgentChatHistoryItem> _chatHistory = [];
        private readonly IRag? _rag;

        private ChatClientAgent? _chatClientAgent;

        private AgentSession? _agentSession;
        private bool _allowFunctionCall;
        private ChatHistorySequence? _chatHistorySequence;

        public ChatAgent(
            IServiceProvider serviceProvider,
            ILogger<ChatAgent> logger) : base(SubAgentNames.ChatAgent, serviceProvider, logger)
        {
            this._rag = serviceProvider.GetService<IRag>();
        }

        public override int Order => 10;

        public override IReadOnlyList<AgentChatHistoryItem> GetChatHistory()
        {
            lock (this._chatHistoryLock)
            {
                return this._chatHistory.ToList();
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
                this._chatHistorySequence = agentBuildConfig.ChatHistorySequence;
                lock (this._chatHistoryLock)
                {
                    this._chatHistory.Clear();
                }

                string instructions = this.BuildInstructions(summaryMemory);
                IChatClient chatClient = this.ServiceProvider.GetRequiredKeyedService<IChatClient>($"LLM_{agentBuildConfig.AgentSetting.ModelName}");

                ILoggerFactory loggerFactory = this.ServiceProvider.GetRequiredService<ILoggerFactory>();
                IChatClient configuredChatClient = chatClient.AsBuilder()
                    .UseFunctionInvocation(loggerFactory, functionClient => functionClient.MaximumIterationsPerRequest = GlobalVariables.MaxFunctionCallDepth)
                    .UsePerServiceCallChatHistoryPersistence()
                    .Build(this.ServiceProvider);


                ChatClientAgentOptions chatClientAgentOptions = new ChatClientAgentOptions
                {
                    Name = SubAgentNames.ChatAgent,
                    Description = $"the agent of {SubAgentNames.ChatAgent}",
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instructions,
                        Temperature = 0.5f,
                        MaxOutputTokens = 40,
                        ResponseFormat = ChatResponseFormat.Text,
                        ToolMode = this._allowFunctionCall ? ChatToolMode.Auto : ChatToolMode.None,
                        Reasoning = new ReasoningOptions
                        {
                            Effort = ReasoningEffort.None,
                            Output = ReasoningOutput.None
                        }
                    },
                    UseProvidedChatClientAsIs = true,
                    RequirePerServiceCallChatHistoryPersistence = true
                };

                List<AIContextProvider> contextProviders = [];
                if (this._rag?.IsReady == true)
                {
                    TextSearchProvider? textSearchProvider = this._rag.Create();
                    if (textSearchProvider is not null)
                    {
                        contextProviders.Add(textSearchProvider);
                    }
                }
                contextProviders.Add(new FunctionToolsContextProvider(
                    agentBuildConfig.SessionPrivateProvider.FunctionToolsContext,
                    () => this._allowFunctionCall));
                chatClientAgentOptions.AIContextProviders = contextProviders;

                this._chatClientAgent = new ChatClientAgent(
                    chatClient: configuredChatClient,
                    options: chatClientAgentOptions,
                    services: this.ServiceProvider
                );

                this._agentSession = this._chatClientAgent.CreateSessionAsync(agentBuildConfig.SessionPrivateProvider.Token).GetAwaiter().GetResult();
                this.Logger.LogInformation(Lang.ChatAgent_Build_Built, this.AgentName, agentBuildConfig.AgentSetting.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.ChatAgent_Build_BuiltFailed, this.AgentName, agentBuildConfig.AgentSetting.ModelName);
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

        /// <summary>
        /// 接收 IntentDetectionResult（未检测到意图），将用户消息转发给 LLM 正常对话
        /// </summary>
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

        /// <summary>
        /// 流式调用LLM，按标点符号分句逐句返回
        /// </summary>
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
            int historyStartIndex = this.GetInMemoryHistory().Count;
            this.AppendHistory(ChatRole.User, userMessage);

            try
            {
                await foreach (AgentResponseUpdate update in this._chatClientAgent.RunStreamingAsync(userMessage, this._agentSession, cancellationToken: token))
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
            finally
            {
                this.AppendAutomaticFunctionHistory(this.GetInMemoryHistory().Skip(historyStartIndex));
                this.AppendHistory(ChatRole.Assistant, allResponse.ToString());
            }
        }

        private List<ChatMessage> GetInMemoryHistory()
        {
            if (this._agentSession is null)
            {
                return [];
            }

            this._agentSession.TryGetInMemoryChatHistory(out List<ChatMessage>? history, jsonSerializerOptions: JsonHelper.OPTIONS);
            return history ?? [];
        }

        /// <summary>
        /// 把 ChatAgent 内由 SDK 自动执行的工具调用，转换成可供记忆总结使用的简化历史记录
        /// </summary>
        /// <param name="messages"></param>
        private void AppendAutomaticFunctionHistory(IEnumerable<ChatMessage> messages)
        {
            Dictionary<string, string> functionNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ChatMessage message in messages)
            {
                foreach (AIContent content in message.Contents)
                {
                    if (content is FunctionCallContent call)
                    {
                        functionNames[call.CallId] = call.Name;
                    }
                    else if (content is FunctionResultContent result)
                    {
                        string functionName = functionNames.TryGetValue(result.CallId, out string? name) ? name : "unknown";
                        this.AppendHistory(ChatRole.Tool, $"工具调用：{functionName}\n工具结果：{this.SerializeHistoryResult(result.Result, result.Exception)}");
                    }
                }
            }
        }

        private string SerializeHistoryResult(object? result, Exception? exception)
        {
            if (exception is not null)
            {
                return exception.Message;
            }
            if (result is null)
            {
                return "(empty)";
            }
            return result is string text ? text : JsonHelper.Serialize(result);
        }

        private void AppendHistory(ChatRole role, string? content)
        {
            if (string.IsNullOrWhiteSpace(content) || this._chatHistorySequence is null)
            {
                return;
            }

            lock (this._chatHistoryLock)
            {
                this._chatHistory.Add(new AgentChatHistoryItem(this._chatHistorySequence.Next(), new ChatMessage(role, content)));
            }
        }

        public override void Dispose()
        {
        }
    }
}
