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
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents.Intent
{
    /// <summary>IntentLlm 第二步：根据 IntentDetectionAgent 输出的函数名和参数，实际调用工具并返回结果</summary>
    internal sealed class FunctionCallAgent : BaseAgent<FunctionCallAgent>
    {
        private PrivateProvider? _sessionPrivateProvider;

        public FunctionCallAgent(IServiceProvider serviceProvider, ILogger<FunctionCallAgent> logger)
            : base(SubAgentNames.FunctionCallAgent, serviceProvider, logger)
        {
        }

        public override int Order => 6;
        public override bool SupportsStreaming => false;

        public override bool Build(LLMAgentBuildConfig buildConfig)
        {
            this._sessionPrivateProvider = buildConfig.SessionPrivateProvider;
            return true;
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

            IList<AITool> tools = this._sessionPrivateProvider?.FunctionTools ?? (IList<AITool>)new List<AITool>();

            if (this.ShouldContinueChat(detection.Function))
            {
                this.Logger.LogDebug("FunctionCallAgent: 未识别到可调用工具，跳过函数调用。");
                await context.SendMessageAsync(new FunctionExecutionResult(string.Empty, null, ToolAction.Silent, detection.UserMessage), token);
                return;
            }

            // 按名称查找工具（大小写不敏感）
            AIFunction? func = tools
                .OfType<AIFunction>()
                .FirstOrDefault(f => string.Equals(f.Name, detection.Function?.Name, StringComparison.OrdinalIgnoreCase));

            if (func is null || detection.Function is null)
            {
                this.Logger.LogWarning("FunctionCallAgent: 未找到工具 '{FunctionName}'，跳过调用。", detection.Function?.Name);
                await context.SendMessageAsync(new FunctionExecutionResult(detection.Function?.Name ?? string.Empty, null, ToolAction.Silent, detection.UserMessage), token);
                return;
            }

            AIFunctionArguments args = this.CreateFunctionArguments(detection.Function);

            object? result = await func.InvokeAsync(args, token);
            FunctionExecutionResult executionResult = await this.NormalizeExecutionResultAsync(detection, result);

            this.Logger.LogDebug("FunctionCallAgent: '{FunctionName}' 调用结果：{Result}", detection.Function?.Name, executionResult.Response ?? "(empty)");
            await context.SendMessageAsync(executionResult, token);
        }

        private AIFunctionArguments CreateFunctionArguments(FunctionMetadata function)
        {
            Dictionary<string, object?> arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (function.Parameters is null)
            {
                return new AIFunctionArguments(arguments);
            }

            foreach (FunctionParameter parameter in function.Parameters)
            {
                arguments[parameter.Name] = this.NormalizeParameterValue(parameter.Value);
            }

            return new AIFunctionArguments(arguments);
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

        private async Task<FunctionExecutionResult> NormalizeExecutionResultAsync(IntentDetectionResult detection, object? result)
        {
            if (result is null)
            {
                return new FunctionExecutionResult(detection.Function?.Name ?? string.Empty, null, ToolAction.Silent, detection.UserMessage);
            }

            object? unwrappedResult = await this.UnwrapTaskLikeResultAsync(result);
            if (unwrappedResult is null)
            {
                return new FunctionExecutionResult(detection.Function?.Name ?? string.Empty, null, ToolAction.Silent, detection.UserMessage);
            }

            Type resultType = unwrappedResult.GetType();
            if (!resultType.IsGenericType || resultType.GetGenericTypeDefinition() != typeof(FunctionReturn<>))
            {
                string llmResponse = this.SerializeResultForLlm(unwrappedResult);
                return new FunctionExecutionResult(detection.Function?.Name ?? string.Empty, llmResponse, ToolAction.Continue, detection.UserMessage);
            }

            ToolAction action = this.ResolveToolAction(detection.Function?.Name, unwrappedResult);
            string? response = this.ResolveResponse(unwrappedResult);

            return new FunctionExecutionResult(
                detection.Function?.Name ?? string.Empty,
                response,
                action,
                detection.UserMessage);
        }

        private async Task<object?> UnwrapTaskLikeResultAsync(object result)
        {
            if (result is Task task)
            {
                await task;
                return task.GetType().GetProperty("Result")?.GetValue(task);
            }

            Type resultType = result.GetType();
            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                var asTaskMethod = resultType.GetMethod("AsTask");
                if (asTaskMethod is null)
                {
                    return result;
                }

                Task? valueTask = asTaskMethod.Invoke(result, null) as Task;
                if (valueTask is null)
                {
                    return result;
                }

                await valueTask;
                return valueTask.GetType().GetProperty("Result")?.GetValue(valueTask);
            }

            return result;
        }

        private ToolAction ResolveToolAction(string? functionName, object functionReturn)
        {
            ToolAction? next = functionReturn.GetType().GetProperty("Next")?.GetValue(functionReturn) as ToolAction?;
            if (next.HasValue)
            {
                return next.Value;
            }

            if (!string.IsNullOrEmpty(functionName)
                && this._sessionPrivateProvider is not null
                && this._sessionPrivateProvider.TryGetFunctionToolRegistration(functionName, out FunctionToolRegistration? registration)
                && registration is not null)
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

        public override void Dispose() { }
    }
}
