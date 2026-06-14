using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal sealed class IntentAgent : BaseAgent<IntentAgent>, IIntentAgent
    {
        private const string INTENT_INSTRUCTIONS = """
You are an intent detector for a smart voice device.
Your job is to determine whether the user wants to trigger a built-in device function (such as playing music, controlling IoT devices, or calling other tools) instead of continuing normal conversation.

If matching tools are available and the user clearly wants to trigger a device function, call the appropriate tool first.

After processing, return JSON only with these two fields:
- intent_detected: boolean — true if the user triggered a device function (i.e., a tool was called); false if the request should continue as normal conversation
- feedback: string — a short, natural Chinese sentence spoken to the user after the function runs; must be empty string when intent_detected is false

Rules:
1. If the user wants normal conversation, knowledge Q&A, storytelling, weather, translation, or anything that does not require a tool call, set intent_detected to false and feedback to empty string.
2. If the user clearly wants to trigger a device function and a matching tool is available, call the tool, then set intent_detected to true and fill feedback with a brief confirmation reply.
3. If the user seems to want a device function but no matching tool is available, set intent_detected to false and feedback to empty string.
4. Never output markdown, explanations, or any text outside the JSON object.
""";
        private const string ACCEPTED_INTENT_MODEL = "INTENT_LLM";

        private ChatClientAgent? _intentClientAgent;

        /// <summary>当前会话的私有提供者，用于在调用时懒加载工具列表</summary>
        private PrivateProvider? _sessionPrivateProvider;

        public IntentAgent(IServiceProvider serviceProvider, ILogger<IntentAgent> logger) : base(SubAgentNames.IntentAgent, serviceProvider, logger)
        {
        }

        public override int Order => 5;

        public override bool SupportsStreaming => false;

        public override bool Build(LLMAgentBuildConfig agentBuildConfig)
        {
            try
            {
                this.Prompt = INTENT_INSTRUCTIONS;

                string intentType = agentBuildConfig.AgentSetting.Config.GetConfigValueOrDefault("Type", "None");
                if (string.Compare(ACCEPTED_INTENT_MODEL, intentType, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    //no intent model required, skip building intent agent
                    //todo: log
                    return true;
                }

                string? selectedLLMModel = agentBuildConfig.AgentSetting.Config.GetValueOrDefault("LLM");
                if (string.IsNullOrEmpty(selectedLLMModel))
                {
                    //todo: log
                    return false;
                }

                IChatClient chatClient = this.ServiceProvider.GetRequiredKeyedService<IChatClient>($"LLM_{selectedLLMModel}");

                ChatClientAgentOptions options = new ChatClientAgentOptions
                {
                    Name = SubAgentNames.IntentAgent,
                    Description = $"the agent of {SubAgentNames.IntentAgent}",
                    ChatOptions = new ChatOptions
                    {
                        Instructions = this.Prompt,
                        Temperature = 0.1f,
                        MaxOutputTokens = 160,
                        ResponseFormat = ChatResponseFormat.ForJsonSchema<IntentResult>()
                    }
                };

                this._intentClientAgent = new ChatClientAgent(
                    chatClient: chatClient,
                    options: options,
                    services: this.ServiceProvider);

                // 保存会话私有提供者，供调用时懒加载工具
                this._sessionPrivateProvider = agentBuildConfig.SessionPrivateProvider;
                //todo
                //this.Logger.LogInformation(Lang.ChatAgent_Build_Built, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                return true;
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
                routeBuilder.AddHandler<string>(this.DetectIntentAsync);
            })
            .SendsMessage<IntentResult>();
        }

        [MessageHandler]
        public async ValueTask DetectIntentAsync(string userMessage, IWorkflowContext workflowContext, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._intentClientAgent is null)
            {
                //todo
                throw new InvalidOperationException("");
            }
            // 调用时从会话提供者中懒加载工具，确保IoT/MCP工具已注册
            ChatClientAgentRunOptions runOptions = new ChatClientAgentRunOptions(new ChatOptions
            {
                ToolMode = ChatToolMode.Auto,
                Tools = (this._sessionPrivateProvider?.FunctionTools.Count > 0)
                    ? this._sessionPrivateProvider.FunctionTools
                    : null
            });
            AgentResponse<IntentResult> intentResponseResult = await this._intentClientAgent.RunAsync<IntentResult>(userMessage, serializerOptions: JsonHelper.OPTIONS, options: runOptions, cancellationToken: token);

            intentResponseResult.Result.UserMessage = userMessage;
            await workflowContext.SendMessageAsync(intentResponseResult.Result, token);
        }

        public override void Dispose()
        {
        }
    }
}