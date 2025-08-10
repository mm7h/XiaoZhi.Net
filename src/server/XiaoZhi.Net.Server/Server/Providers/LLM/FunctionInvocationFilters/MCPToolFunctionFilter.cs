using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
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
            _logger = logger;
        }
        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            if (!string.IsNullOrEmpty(context.Function.PluginName) && context.Kernel.Data.TryGetValue("session", out var data) && data is not null && data is Session session)
            {
                if (_subMCPClientTypeNames.Contains(context.Function.PluginName))
                {
                    try
                    {
                        ISubMcpClient? subMcpClient = session.McpClient.GetSubMcpClient(context.Function.PluginName);

                        if (subMcpClient is null)
                        {
                            throw new InvalidOperationException($"SubMcpClient with type name '{context.Function.PluginName}' not found.");
                        }

                        string callResult = await subMcpClient.CallMcpToolAsync(context.Function.Name, context.Arguments);
                        context.Result = new FunctionResult(context.Result, callResult);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to invoke the MCP tool function: {FunctionName} in plugin: {PluginName}.", context.Function.Name, context.Function.PluginName);
                        string failedMessage = $"Failed to invoke the MCP tool function: {context.Function.Name} in plugin: {context.Function.PluginName}, and the error message is: {ex.Message}.";
                        context.Result = new FunctionResult(context.Result, failedMessage);
                    }
                }
                else if (context.Function.PluginName.StartsWith(SubMCPClientTypeNames.DeviceIoTClient))
                {
                    try
                    {
                        if (context.Function.Name.ToLower().StartsWith("get_"))
                        {
                            // 获取iot属性值
                            Type? returnValueType = context.Function.Metadata.ReturnParameter.ParameterType;
                            if (returnValueType is null)
                            {
                                throw new InvalidOperationException($"Return type for function '{context.Function.Name}' in plugin '{context.Function.PluginName}' is not specified.");
                            }
                            else
                            {
                                object? result = session.IoTClient.GetIoTPropertyStatus(context.Function.Name, returnValueType);
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
                                await session.IoTClient.ExecuteIoTCommand(iotDeviceComponentName, context.Function.Name, context.Function.Metadata.Parameters, context.Arguments);

                                context.Result = new FunctionResult(context.Result, "Invoke the iot command successfully.");
                            }
                            else
                            {
                                throw new InvalidOperationException($"Invalid IoT component name format in plugin '{context.Function.PluginName}'.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to invoke the IoT tool function: {FunctionName} in plugin: {PluginName}.", context.Function.Name, context.Function.PluginName);
                        string failedMessage = $"Failed to invoke the IoT tool function: {context.Function.Name} in plugin: {context.Function.PluginName}, and the error message is: {ex.Message}.";
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
