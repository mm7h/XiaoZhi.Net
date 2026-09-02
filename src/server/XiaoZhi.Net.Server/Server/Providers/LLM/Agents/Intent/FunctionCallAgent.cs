using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Providers.LLM.Utils;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents.Intent
{
    internal sealed class FunctionCallAgent : BaseAgent<FunctionCallAgent>
    {
        private readonly object _chatHistoryLock = new object();
        private readonly List<AgentChatHistoryItem> _chatHistory = [];

        private ChatHistorySequence? _chatHistorySequence;

        public FunctionCallAgent(IServiceProvider serviceProvider, ILogger<FunctionCallAgent> logger)
            : base(SubAgentNames.FunctionCallAgent, serviceProvider, logger)
        {
        }

        public override int Order => 6;
        public override bool SupportsStreaming => false;

        public override bool Build(LLMAgentBuildConfig buildConfig)
        {
            this._chatHistorySequence = buildConfig.ChatHistorySequence;
            lock (this._chatHistoryLock)
            {
                this._chatHistory.Clear();
            }
            return true;
        }

        public override IReadOnlyList<AgentChatHistoryItem> GetChatHistory()
        {
            lock (this._chatHistoryLock)
            {
                return this._chatHistory.ToList();
            }
        }

        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return protocolBuilder.ConfigureRoutes(routeBuilder =>
            {
                routeBuilder.AddHandler<IntentDetectionResult>(this.InvokeFunctionAsync);
            })
            .SendsMessage<FunctionExecutionResult>();
        }

        [MessageHandler]
        public async ValueTask InvokeFunctionAsync(IntentDetectionResult detection, IWorkflowContext context, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }

            this.AppendHistory(ChatRole.User, detection.UserMessage);

            if (this.ShouldContinueChat(detection.Function))
            {
                this.Logger.LogDebug("FunctionCallAgent: 未识别到可调用工具，跳过函数调用。");
                await context.SendMessageAsync(new FunctionExecutionResult(string.Empty, null, ToolAction.Silent, detection.UserMessage), token);
                return;
            }

            FunctionMetadata function = detection.Function!;

            FunctionToolRegistration? registration = detection.ResolvedRegistration;
            AIFunction? func = registration?.Function;

            if (func is null)
            {
                this.Logger.LogWarning("FunctionCallAgent: 本轮快照中未找到工具 '{FunctionName}'，跳过调用。", function.Name);
                this.AppendToolHistory(function.Name, "工具未找到。");
                await context.SendMessageAsync(new FunctionExecutionResult(function.Name, null, ToolAction.Silent, detection.UserMessage), token);
                return;
            }

            AIFunctionArguments args = this.CreateFunctionArguments(function);

            object? result;
            try
            {
                result = await func.InvokeAsync(args, token);
            }
            catch (Exception ex)
            {
                this.AppendToolHistory(function.Name, ex.Message);
                throw;
            }
            FunctionExecutionResult executionResult = this.NormalizeExecutionResult(detection, result, registration);
            this.AppendToolHistory(executionResult.FunctionName, executionResult.Response ?? "(empty)");

            this.Logger.LogDebug("FunctionCallAgent: '{FunctionName}' 调用结果：{Result}", detection.Function?.Name, executionResult.Response ?? "(empty)");
            await context.SendMessageAsync(executionResult, token);
        }

        private AIFunctionArguments CreateFunctionArguments(FunctionMetadata function)
        {
            Dictionary<string, object?> arguments = this.BuildArgumentDictionary(function.Parameters);
            return new AIFunctionArguments(arguments);
        }

        private Dictionary<string, object?> BuildArgumentDictionary(IList<FunctionParameter>? parameters)
        {
            Dictionary<string, object?> arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (parameters is null)
            {
                return arguments;
            }

            foreach (FunctionParameter parameter in parameters)
            {
                arguments[parameter.Name] = this.NormalizeParameterValue(parameter.Value);
            }

            return arguments;
        }

        private bool ShouldContinueChat(FunctionMetadata? function)
        {
            return function is null || string.IsNullOrWhiteSpace(function.Name);
        }

        private object? NormalizeParameterValue(object? value)
        {
            if (value is JsonElement jsonElement)
            {
                return jsonElement.ValueKind switch
                {
                    JsonValueKind.String => jsonElement.GetString(),
                    JsonValueKind.Number => jsonElement.TryGetInt64(out long longValue)
                        ? longValue
                        : jsonElement.TryGetDouble(out double doubleValue)
                            ? doubleValue
                            : jsonElement.GetRawText(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => jsonElement.GetRawText(),
                };
            }

            return value;
        }

        private FunctionExecutionResult NormalizeExecutionResult(IntentDetectionResult detection, object? result, FunctionToolRegistration? registration)
        {
            if (result is null)
            {
                return new FunctionExecutionResult(detection.Function?.Name ?? string.Empty, null, ToolAction.Silent, detection.UserMessage);
            }

            Type resultType = result.GetType();
            if (!resultType.IsGenericType || resultType.GetGenericTypeDefinition() != typeof(FunctionReturn<>))
            {
                string llmResponse = this.SerializeResultForLlm(result);
                return new FunctionExecutionResult(detection.Function?.Name ?? string.Empty, llmResponse, ToolAction.Continue, detection.UserMessage);
            }

            ToolAction action = this.ResolveToolAction(result, registration);
            string? response = this.ResolveResponse(result);

            return new FunctionExecutionResult(
                detection.Function?.Name ?? string.Empty,
                response,
                action,
                detection.UserMessage);
        }

        private ToolAction ResolveToolAction(object functionReturn, FunctionToolRegistration? registration)
        {
            ToolAction? next = functionReturn.GetType().GetProperty("Next")?.GetValue(functionReturn) as ToolAction?;
            if (next.HasValue)
            {
                return next.Value;
            }

            if (registration is not null)
            {
                return registration.DefaultAction;
            }

            return ToolAction.Continue;
        }

        private string? ResolveResponse(object functionReturn)
        {
            return functionReturn.GetType().GetProperty("Response")?.GetValue(functionReturn) as string;
        }

        private string SerializeResultForLlm(object result)
        {
            if (result is string str)
            {
                return str;
            }

            if (result is JsonElement jsonElement)
            {
                return jsonElement.GetRawText();
            }

            Type resultType = result.GetType();
            if (resultType.IsPrimitive || result is decimal)
            {
                return Convert.ToString(result) ?? string.Empty;
            }

            return JsonHelper.Serialize(result);
        }

        private void AppendToolHistory(string functionName, string result)
        {
            this.AppendHistory(ChatRole.Tool, $"工具调用：{functionName}\n工具结果：{result}");
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

        public override void Dispose() { }
    }
}
