using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.LLM.AIContextProviders;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents.Intent
{
    internal sealed class IntentDetectionAgent : BaseAgent<IntentDetectionAgent>
    {
        private const string ACCEPTED_INTENT_MODEL = "IntentLlm";

        private const string INTENT_DETECTION_PROMPT = """
    你是一个智能语音设备的意图识别助手。

    你的任务是分析用户最后一句话，判断是否应该调用已注入或者已给出的某个可用的函数工具。

    严格输出要求：
    1. 直接输出纯 JSON 文本，严禁使用反引号（`）或三反引号（```）包裹 JSON。
    2. 绝对不要输出 markdown、解释、自然语言、代码块或额外文本。
    3. 你只能输出可被 IntentDetectionResult 反序列化的纯 JSON 对象。
    4. JSON 顶层字段必须严格符合 IntentDetectionResult 结构：
       - detected（bool，必填）：是否检测到意图。
       - function（对象|null，必填）：当 detected 为 true 时，必须包含 FunctionMetadata 对象（name/description/parameters/inputJsonSchema）；当 detected 为 false 时，必须为 null。
       - userMessage（string，必填）：用户的原始消息文本。
    5. 当 detected 为 false 时，function 字段必须为 null。
    6. 当 detected 为 true 时，function 中的 name 必须与可用函数名完全一致；只有在函数确实需要参数时才返回 parameters。
    7. parameters 必须是参数数组；每个参数对象至少包含 name 和 value；只有在你能明确判断参数类型时才补充 type；不要臆造不存在的参数。

    意图判断规则：
    1. 只有当用户明确想触发某个可用函数时，才设置 detected 为 true；否则 detected 必须为 false。
    2. 如果用户是在闲聊、追问原因、询问做法、表达情绪，或意图不明确，应返回 detected:false。
    3. 对于时间、日期、农历、节气、城市、位置等基础信息，只有在可用函数列表中确实存在对应函数时才调用；否则返回 detected:false。
    4. 如果用户是在问"怎么退出""为什么退出""如何关闭"这类解释性问题，不要误判成执行退出或关闭命令；这种情况通常返回 detected:false。
    5. 如果没有匹配函数，返回 detected:false。
    6. 如果用户一句话里包含多个指令，仍然只选择当前最核心、最明确的一个函数；如果无法唯一确定，则返回 detected:false。
    7. 优先保证函数名和参数准确，无法确认时不要猜测。
    8. 当用户明确告别或要求结束当前对话（如“再见”“拜拜”“先聊到这”“结束对话”）时，如果可用函数中存在用于结束会话的工具，必须调用该工具，并提供自然、简短的告别语参数。

    返回示例：
    1. 普通聊天：{"detected":false,"function":null,"userMessage":"今天我遇到了烦心事，伤透了"}
    2. 命中有参函数：{"detected":true,"function":{"name":"set_volume","parameters":[{"name":"level","value":50,"type":"integer"}]},"userMessage":"把音量调到50"}
    3. 命中无参函数：{"detected":true,"function":{"name":"get_files","parameters":[]},"userMessage":"获取一下文件列表"}
    
    可用的函数列表如下：

    """;

        private ChatClientAgent? _intentClientAgent;
        private PrivateProvider? _sessionPrivateProvider;
        private readonly AsyncLocal<string?> _activeIntentInstructions = new AsyncLocal<string?>();

        public IntentDetectionAgent(IServiceProvider serviceProvider, ILogger<IntentDetectionAgent> logger)
            : base(SubAgentNames.IntentDetectionAgent, serviceProvider, logger)
        {
        }

        public override int Order => 5;
        public override bool SupportsStreaming => false;

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
                if (string.IsNullOrWhiteSpace(selectedLLMModel))
                {
                    this.Logger.LogError("IntentDetectionAgent: 未配置 LLM 模型。");
                    return false;
                }

                IChatClient chatClient = this.ServiceProvider.GetRequiredKeyedService<IChatClient>($"LLM_{selectedLLMModel}");
                this._sessionPrivateProvider = buildConfig.SessionPrivateProvider;
                this._sessionPrivateProvider.FunctionToolsContext.ConfigureIntentInstructions(INTENT_DETECTION_PROMPT);
                ChatClientAgentOptions options = new ChatClientAgentOptions
                {
                    Name = SubAgentNames.IntentDetectionAgent,
                    Description = $"the agent of {SubAgentNames.IntentDetectionAgent}",
                    ChatOptions = new ChatOptions
                    {
                        Temperature = 0.1f,
                        MaxOutputTokens = 200,
                        ResponseFormat = ChatResponseFormat.ForJsonSchema<IntentDetectionResult>(),
                        Reasoning = new ReasoningOptions
                        {
                            Effort = ReasoningEffort.None,
                            Output = ReasoningOutput.None
                        }
                    }
                };
                options.AIContextProviders = [new IntentInstructionsContextProvider(() => this._activeIntentInstructions.Value ?? this._sessionPrivateProvider.FunctionToolsContext.Capture().IntentInstructions)];

                this._intentClientAgent = new ChatClientAgent(
                    chatClient: chatClient,
                    options: options,
                    services: this.ServiceProvider);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "IntentDetectionAgent Build 失败。");
                return false;
            }
        }

        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return protocolBuilder.ConfigureRoutes(routeBuilder =>
            {
                routeBuilder.AddHandler<WorkflowPreInputs>(this.DetectIntentAsync);
            })
            .SendsMessage<IntentDetectionResult>();
        }

        [MessageHandler]
        public async ValueTask DetectIntentAsync(WorkflowPreInputs preInput, IWorkflowContext context, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }

            // 未初始化（非 IntentLlm 模式），输出无意图结果
            if (this._intentClientAgent is null)
            {
                await context.SendMessageAsync(this.CreateEmptyDetectionResult(preInput.UserMessage), token);
                return;
            }

            if (this._sessionPrivateProvider is null)
            {
                await context.SendMessageAsync(this.CreateEmptyDetectionResult(preInput.UserMessage), token);
                return;
            }

            var toolContext = this._sessionPrivateProvider.FunctionToolsContext.Capture();
            if (toolContext.Tools.Count == 0)
            {
                await context.SendMessageAsync(this.CreateEmptyDetectionResult(preInput.UserMessage), token);
                return;
            }

            string? previousIntentInstructions = this._activeIntentInstructions.Value;
            this._activeIntentInstructions.Value = toolContext.IntentInstructions;
            try
            {
                AgentResponse<IntentDetectionResult> response = await this._intentClientAgent.RunAsync<IntentDetectionResult>(
                    preInput.UserMessage,
                    serializerOptions: JsonHelper.OPTIONS,
                    cancellationToken: token);

                if (response.Result is null || response.Result.Function is null)
                {
                    IntentDetectionResult result = this.CreateEmptyDetectionResult(preInput.UserMessage);
                    this.Logger.LogDebug("IntentDetectionAgent: LLM 没有返回可用结果，已转换为无意图结果。");
                    await context.SendMessageAsync(result, token);
                }
                else
                {
                    IntentDetectionResult result = response.Result;
                    if (!toolContext.Registrations.TryGetValue(result.Function.Name, out FunctionToolRegistration? registration))
                    {
                        this.Logger.LogWarning("IntentDetectionAgent: 工具 '{Function}' 在本轮快照中不存在，已转换为无意图结果。", result.Function.Name);
                        await context.SendMessageAsync(this.CreateEmptyDetectionResult(preInput.UserMessage), token);
                        return;
                    }

                    this.Logger.LogDebug("IntentDetectionAgent: function={Function}", result.Function.Name);
                    await context.SendMessageAsync(result with { ResolvedRegistration = registration }, token);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                this.Logger.LogWarning("IntentDetectionAgent: LLM 返回了无效的 JSON（可能被 markdown 包裹），回退为无意图结果。");
                await context.SendMessageAsync(this.CreateEmptyDetectionResult(preInput.UserMessage), token);
            }
            finally
            {
                this._activeIntentInstructions.Value = previousIntentInstructions;
            }
        }

        private IntentDetectionResult CreateEmptyDetectionResult(string userMessage)
        {
            return new IntentDetectionResult(false, null, userMessage);
        }

        public override void Dispose() { }
    }
}
