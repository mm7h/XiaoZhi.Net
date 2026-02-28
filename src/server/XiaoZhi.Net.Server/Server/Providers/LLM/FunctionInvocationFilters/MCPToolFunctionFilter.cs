using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.MCP;

namespace XiaoZhi.Net.Server.Providers.LLM.FunctionInvocationFilters
{
    internal class MCPToolFunctionFilter : IFunctionInvocationFilter
    {
        private readonly HashSet<string> _subMCPClientTypeNames = new HashSet<string>(3) { SubMCPClientTypeNames.DeviceMcpClient, SubMCPClientTypeNames.McpEndpointClient, SubMCPClientTypeNames.ServerMcpClient };
        private readonly ILogger _logger;

        private const string IOT_COMPONENT_PATTERN = @"^" + SubMCPClientTypeNames.DeviceIoTClient + @"_(.+?)_\d+$";

        public MCPToolFunctionFilter(ILogger<MCPToolFunctionFilter> logger)
        {
            this._logger = logger;
        }
        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            if (!string.IsNullOrEmpty(context.Function.PluginName) && context.Kernel.Data.TryGetValue("session", out var data) && data is not null && data is Session session)
            {
                // Check if session is cancelled
                if (session.SessionCtsToken.IsCancellationRequested)
                {
                    this._logger.LogDebug(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_FunctionCancelled, context.Function.Name);
                    throw new OperationCanceledException(session.SessionCtsToken);
                }
                
                if (_subMCPClientTypeNames.Contains(context.Function.PluginName))
                {
                    try
                    {
                        if (session.PrivateProvider.McpClient is null)
                        {
                            this._logger.LogWarning(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_McpClientNotInit, session.DeviceId);
                            await next(context);
                            return;
                        }
                        ISubMcpClient? subMcpClient = session.PrivateProvider.McpClient.GetSubMcpClient(context.Function.PluginName);

                        if (subMcpClient is null)
                        {
                            throw new InvalidOperationException(string.Format(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_SubMcpClientNotFound, context.Function.PluginName));
                        }

                        string callResult = await subMcpClient.CallMcpToolAsync(context.Function.Name, context.Arguments);
                        context.Result = new FunctionResult(context.Result, callResult);
                    }
                    catch (OperationCanceledException)
                    {
                        this._logger.LogDebug(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_McpCancelled, context.Function.Name);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        this._logger.LogError(ex, Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailed, context.Function.Name, context.Function.PluginName);
                        string failedMessage = string.Format(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailedDetail, context.Function.Name, context.Function.PluginName, ex.Message);
                        context.Result = new FunctionResult(context.Result, failedMessage);
                    }
                }
                else if (context.Function.PluginName.StartsWith(SubMCPClientTypeNames.DeviceIoTClient))
                {
                    try
                    {
                        if (session.PrivateProvider.IoTClient is null)
                        {
                            this._logger.LogWarning(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_IoTClientNotInit, session.DeviceId);
                            await next(context);
                            return;
                        }

                        if (context.Function.Name.ToLower().StartsWith("get_"))
                        {
                            // 获取iot属性值
                            Type? returnValueType = context.Function.Metadata.ReturnParameter.ParameterType;
                            if (returnValueType is null)
                            {
                                throw new InvalidOperationException(string.Format(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_ReturnTypeNotSpecified, context.Function.Name, context.Function.PluginName));
                            }
                            else
                            {
                                object? result = session.PrivateProvider.IoTClient.GetIoTPropertyStatus(context.Function.Name, returnValueType);
                                context.Result = new FunctionResult(context.Result, result);
                            }
                        }
                        else
                        {
                            // 执行iot命令
                            Match match = Regex.Match(context.Function.PluginName, IOT_COMPONENT_PATTERN);

                            if (match.Success)
                            {
                                string iotDeviceComponentName = match.Groups[1].Value;
                                await session.PrivateProvider.IoTClient.ExecuteIoTCommand(iotDeviceComponentName, context.Function.Name, context.Function.Metadata.Parameters, context.Arguments);

                                context.Result = new FunctionResult(context.Result, Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTSuccess);
                            }
                            else
                            {
                                throw new InvalidOperationException(string.Format(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_InvalidIoTName, context.Function.PluginName));
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        this._logger.LogDebug(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_IoTCancelled, context.Function.Name);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        this._logger.LogError(ex, Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailed, context.Function.Name, context.Function.PluginName);
                        string failedMessage = string.Format(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailedDetail, context.Function.Name, context.Function.PluginName, ex.Message);
                        context.Result = new FunctionResult(context.Result, failedMessage);
                    }
                }
                else
                {
                    await next(context);
                }
            }
            else
            {
                await next(context);
            }
        }
    }
}
