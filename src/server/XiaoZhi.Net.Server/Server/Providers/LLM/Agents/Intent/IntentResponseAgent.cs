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
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents.Intent
{
    internal sealed class IntentResponseAgent : BaseAgent<IntentResponseAgent>
    {
        private const string ACCEPTED_INTENT_MODEL = "IntentLlm";
        private const string RESPONSE_SYSTEM_PROMPT = """
你是一个智能语音设备的意图识别结果播报助手。

你的任务不是闲聊，也不是继续推理工具调用，而是把“已经执行完成的工具结果”转换成适合语音播报的一句话或两句话中文回复。

请严格遵守以下规则：
1. 只基于提供的用户指令、工具名称和工具执行结果进行回复，不要编造额外信息。
2. 回复要口语化、自然、简短，适合直接 TTS 播报。
3. 优先直接告知执行结果，不要重复复述工具名称、参数、内部流程或 JSON 结构。
4. 如果工具结果已经是完整自然语言，就只做必要润色，不要改写出新的含义。
5. 如果工具结果表示失败、异常或未完成，请明确告知用户当前结果，并保持措辞平和。
6. 除非用户原始意图明显要求进一步说明，否则不要展开解释，不要追加建议。
7. 不要输出 markdown、列表、标题、引号包裹文本，也不要输出表情。

输出要求：
- 仅输出最终给用户播报的中文文本。
- 控制在 1 到 2 句之内。
""";

        private ChatClientAgent? _chatClientAgent;
        private AgentSession? _agentSession;
        public IntentResponseAgent(IServiceProvider serviceProvider, ILogger<IntentResponseAgent> logger)
            : base(SubAgentNames.IntentResponseAgent, serviceProvider, logger)
        {
        }

        public override int Order => 7;
        public override bool SupportsStreaming => true;

        public override bool Build(LLMAgentBuildConfig buildConfig)
        {
            try
            {
                string intentType = buildConfig.AgentSetting.Config.GetConfigValueOrDefault("Type", "None");
                if (string.Compare(ACCEPTED_INTENT_MODEL, intentType, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // 非 IntentLlm 模式，跳过初始化（不会被工作流调用）
                    return true;
                }

                string? selectedLLMModel = buildConfig.AgentSetting.Config.GetValueOrDefault("LLM");
                if (string.IsNullOrEmpty(selectedLLMModel))
                {
                    this.Logger.LogError("IntentDetectionAgent: 未配置 LLM 模型。");
                    return false;
                }
                IChatClient chatClient = this.ServiceProvider.GetRequiredKeyedService<IChatClient>($"LLM_{selectedLLMModel}");

                ChatClientAgentOptions options = new ChatClientAgentOptions
                {
                    Name = SubAgentNames.IntentResponseAgent,
                    Description = $"the agent of {SubAgentNames.IntentResponseAgent}",
                    ChatOptions = new ChatOptions
                    {
                        Instructions = RESPONSE_SYSTEM_PROMPT,
                        Temperature = 0.5f,
                        MaxOutputTokens = 150,
                        ResponseFormat = ChatResponseFormat.Text
                    }
                };

                this._chatClientAgent = new ChatClientAgent(
                    chatClient: chatClient,
                    options: options,
                    services: this.ServiceProvider);
                this._agentSession = this._chatClientAgent.CreateSessionAsync(buildConfig.SessionPrivateProvider.Token).GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "IntentResponseAgent Build 失败。");
                return false;
            }
        }

        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return protocolBuilder.ConfigureRoutes(routeBuilder =>
            {
                routeBuilder.AddHandler<IntentDetectionResult>(this.GenerateNoIntentDetectedResultResponseAsync)
                            .AddHandler<FunctionExecutionResult>(this.GenerateFunctionExecutionResultResponseAsync);
            })
            .YieldsOutput<string>()
            .YieldsOutput<IntentDetectionResult>();
        }

        [MessageHandler]
        public async ValueTask GenerateNoIntentDetectedResultResponseAsync(IntentDetectionResult intentDetectionResult, IWorkflowContext context, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (!intentDetectionResult.Detected)
            {
                await context.YieldOutputAsync(intentDetectionResult, token);
            }
            else
            {
                this.Logger.LogWarning("IntentResponseAgent 收到意图检测结果，但意图被检测为存在，无法生成无意图回复。");
                return;
            }
        }

        [MessageHandler]
        public async ValueTask GenerateFunctionExecutionResultResponseAsync(FunctionExecutionResult executionResult, IWorkflowContext context, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatClientAgent is null || this._agentSession is null)
            {
                throw new InvalidOperationException("IntentResponseAgent 未初始化。");
            }

            if (executionResult.Action == ToolAction.Silent || string.IsNullOrWhiteSpace(executionResult.Response))
            {
                return;
            }

            string userContent = $"用户的问题或指令：{executionResult.UserMessage}{Environment.NewLine}工具名称：{executionResult.FunctionName}{Environment.NewLine}工具执行结果：{executionResult.Response}{Environment.NewLine}请简洁回复。";

            await foreach (string sentence in this.StreamResponseSentencesAsync(userContent, token))
            {
                await context.YieldOutputAsync(sentence, token);
            }
        }

        private async IAsyncEnumerable<string> StreamResponseSentencesAsync(string userMessage, [EnumeratorCancellation] CancellationToken token)
        {
            if (this._chatClientAgent is null || this._agentSession is null)
            {
                throw new InvalidOperationException("IntentResponseAgent 未初始化。");
            }

            StringBuilder segmentResponse = new StringBuilder();

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
                    yield return sentence;

                    string remaining = currentSegment.Substring(splitPosition);
                    segmentResponse.Clear();
                    segmentResponse.Append(remaining);
                    currentSegment = remaining;
                    match = DialogueHelper.SENTENCE_SPLIT_REGEX.Match(currentSegment);
                }
            }

            // 流结束后，将剩余文本（不以标点结尾的尾句）也发出
            string tail = segmentResponse.ToString().Trim();
            if (!string.IsNullOrEmpty(tail))
            {
                yield return tail;
            }
        }

        public override void Dispose() { }
    }
}
