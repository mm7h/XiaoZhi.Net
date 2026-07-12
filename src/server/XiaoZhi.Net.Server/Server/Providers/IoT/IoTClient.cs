using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Models;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Providers.IoT
{
    internal class IoTClient : BaseProvider<IoTClient, Session>, IIoTClient
    {
        private readonly IList<IoTProperty> _iotProperties;

        public IoTClient(ILogger<IoTClient> logger) : base(logger)
        {
            this._iotProperties = new List<IoTProperty>();
        }

        public Session CurrentSession { get; private set; } = null!;

        public override string ModelName => SubMCPClientTypeNames.DeviceIoTClient;
        public override string ProviderType => "IoTClient";


        public override bool Build(Session session)
        {
            this.CurrentSession = session;
            return true;
        }

        public void HandleIoTMessage(JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("descriptors", out var descriptors) && descriptors is not null && descriptors is JsonArray descriptorsArray)
            {
                this.RegisterIoTTools(descriptorsArray);
            }
            else if (jsonObject.TryGetPropertyValue("states", out var states) && states is not null && states is JsonArray statesArray)
            {
                this.RegisterIoTPropertyStatus(statesArray);
            }
        }

        public async Task<FunctionReturn<string>> ExecuteIoTCommandAsync(string iotDeviceComponentName, string functionName, IReadOnlyDictionary<string, object?> arguments, IReadOnlyList<IoTParameterInfo> parameterInfos, CancellationToken cancellationToken = default)
        {
            IDictionary<string, object?> resultArgs = new Dictionary<string, object?>();
            foreach (var argument in arguments)
            {
                IoTParameterInfo? paramInfo = parameterInfos.FirstOrDefault(p => string.Equals(p.Name, argument.Key, StringComparison.OrdinalIgnoreCase));
                if (paramInfo is not null)
                {
                    object? val = IoTTypeMappingHelper.ConvertValue(argument.Value, paramInfo.ParameterType);
                    resultArgs.Add(argument.Key, val);
                    this.UpdateIoTPropertyStatus(iotDeviceComponentName, paramInfo.Name, val, paramInfo.ParameterType);
                }
                else
                {
                    resultArgs.Add(argument.Key, argument.Value);
                    this.UpdateIoTPropertyStatus(iotDeviceComponentName, argument.Key, argument.Value);
                }
            }
            var command = new
            {
                Name = iotDeviceComponentName,
                Method = functionName,
                Parameters = resultArgs
            };
            await this.SendIoTMessageAsync(command);
            return new FunctionReturn<string>
            {
                Next = ToolAction.Continue,
                Result = "OK",
                Response = "操作已执行。"
            };
        }

        public FunctionReturn<object?> GetIoTPropertyStatus(string functionName, Type returnValueType)
        {
            List<string> splitedItems = functionName.Split("_", StringSplitOptions.RemoveEmptyEntries).ToList();
            string iotDeviceComponentName = splitedItems[1];
            string propName = splitedItems[2];
            IoTProperty? property = this._iotProperties.FirstOrDefault(i => i.IoTComponentName == iotDeviceComponentName && i.Name == propName && i.Type == returnValueType);
            object? result = property?.StatusValue ?? null;
            return new FunctionReturn<object?>
            {
                Next = ToolAction.Continue,
                Result = result,
                Response = result?.ToString()
            };
        }

        private void UpdateIoTPropertyStatus(string iotDeviceComponentName, string propName, object? val, Type? valType = null)
        {
            IoTProperty? property = this._iotProperties.FirstOrDefault(i => i.IoTComponentName == iotDeviceComponentName.ToLower() && i.Name == propName.ToLower() && i.Type == (valType is not null ? valType : typeof(string)));
            if (property is not null)
            {
                property.StatusValue = val;
            }
        }

        private void RegisterIoTTools(JsonArray descriptors)
        {
            int index = 1;
            foreach (JsonNode? descriptor in descriptors)
            {
                if (descriptor is null)
                {
                    continue;
                }

                string iotDeviceComponentName = descriptor["name"]?.GetValue<string>() ?? "";

                if (string.IsNullOrWhiteSpace(iotDeviceComponentName))
                {
                    continue;
                }

                string deviceDescription = descriptor["description"]?.GetValue<string>() ?? "";
                index++;

                // 注册属性读取工具（AIFunction 闭包，直接读取本地属性状态）
                JsonObject properties = descriptor["properties"] as JsonObject ?? new JsonObject();
                foreach (var property in properties)
                {
                    if (property.Value is JsonObject propObj)
                    {
                        string propName = property.Key;
                        string propDescription = propObj["description"]?.GetValue<string>() ?? string.Empty;
                        string propType = propObj["type"]?.GetValue<string>() ?? "string";

                        string capturedComponent = iotDeviceComponentName;
                        string capturedProp = propName;
                        Type capturedType = IoTTypeMappingHelper.GetIoTType(propType);
                        IoTClient capturedClient = this;

                        string funcName = $"get_{iotDeviceComponentName.ToLower()}_{propName.ToLower()}";

                        AIFunction propertyFunc = AIFunctionFactory.Create(
                            () =>
                            {
                                // 直接按 component 和 prop 名查找属性状态
                                IoTProperty? prop = capturedClient._iotProperties.FirstOrDefault(
                                    i => i.IoTComponentName == capturedComponent.ToLower()
                                      && i.Name == capturedProp.ToLower()
                                      && i.Type == capturedType);
                                object? statusValue = prop?.StatusValue;
                                return new FunctionReturn<object?>
                                {
                                    Next = ToolAction.Continue,
                                    Result = statusValue,
                                    Response = statusValue?.ToString()
                                };
                            },
                            funcName,
                            string.Format(Lang.IoTClient_RegisterIoTTools_FunctionDescription, propDescription));

                        FunctionMetadata propertyMetadata = propertyFunc.ToFunctionMetadata();
                        this.CurrentSession.PrivateProvider.AddFunctionToolRegistration(new FunctionToolRegistration(propertyFunc, propertyMetadata, ToolAction.Continue));
                        this.RegisterIoTProperties(iotDeviceComponentName, propName, propObj);
                    }
                }

                // 注册方法调用工具（AIFunction 闭包，接收 JSON 参数并路由执行）
                JsonObject methods = descriptor["methods"] as JsonObject ?? new JsonObject();
                foreach (var method in methods)
                {
                    if (method.Value is JsonObject methodObj)
                    {
                        string methodName = method.Key;
                        string methodDescription = methodObj["description"]?.GetValue<string>() ?? string.Empty;

                        List<IoTParameterInfo> methodParameters = new List<IoTParameterInfo>();
                        JsonObject parameters = methodObj["parameters"] as JsonObject ?? new JsonObject();
                        foreach (var parameter in parameters)
                        {
                            if (parameter.Value is JsonObject paramObj)
                            {
                                string paramName = parameter.Key;
                                string paramDescription = paramObj["description"]?.GetValue<string>() ?? string.Empty;
                                string paramType = paramObj["type"]?.GetValue<string>() ?? "string";

                                IoTParameterInfo paramInfo = new IoTParameterInfo(
                                    paramName,
                                    paramDescription,
                                    IoTTypeMappingHelper.GetIoTType(paramType));
                                methodParameters.Add(paramInfo);
                            }
                        }

                        string capturedComponent = iotDeviceComponentName;
                        string capturedMethod = methodName;
                        List<IoTParameterInfo> capturedParams = new List<IoTParameterInfo>(methodParameters);
                        IoTClient capturedClient = this;

                        string inputJsonSchema = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = parameters,
                            ["required"] = new JsonArray()
                        }.ToJsonString(JsonHelper.OPTIONS);

                        ProxyAIFunction methodFunc = new ProxyAIFunction(
                            methodName,
                            methodDescription,
                            JsonHelper.ToJsonElement(inputJsonSchema),
                            async (AIFunctionArguments args, CancellationToken ct) =>
                                await capturedClient.ExecuteIoTCommandAsync(capturedComponent, capturedMethod, args, capturedParams, ct));

                        FunctionMetadata methodMetadata = methodFunc.ToFunctionMetadata();
                        this.CurrentSession.PrivateProvider.AddFunctionToolRegistration(new FunctionToolRegistration(methodFunc, methodMetadata, ToolAction.Continue));
                    }
                }
            }
        }

        private void RegisterIoTPropertyStatus(JsonArray states)
        {
            foreach (var status in states)
            {
                if (status is null)
                {
                    continue;
                }

                string iotDeviceComponentName = status["name"]?.GetValue<string>() ?? "";

                if (string.IsNullOrWhiteSpace(iotDeviceComponentName))
                {
                    continue;
                }

                JsonObject properties = status["state"] as JsonObject ?? new JsonObject();
                foreach (var property in properties)
                {
                    string propName = property.Key;
                    if (property.Value is null)
                    {
                        this.SetIoTPropertyStatusValue(iotDeviceComponentName, propName, "");
                        continue;
                    }
                    switch (property.Value.GetValueKind())
                    {
                        case JsonValueKind.Number:
                            {
                                decimal numberValue = property.Value.GetValue<decimal>();
                                this.SetIoTPropertyStatusValue(iotDeviceComponentName, propName, numberValue);
                                break;
                            }
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            {
                                bool boolValue = property.Value.GetValue<bool>();
                                this.SetIoTPropertyStatusValue(iotDeviceComponentName, propName, boolValue);
                                break;
                            }
                        default:
                            string strValue = property.Value.GetValue<string>();
                            this.SetIoTPropertyStatusValue(iotDeviceComponentName, propName, strValue);
                            break;
                    }
                }

            }
        }

        private void RegisterIoTProperties(string iotDeviceComponentName, string propName, JsonObject propObj)
        {
            string type = propObj["type"]?.GetValue<string>() ?? "";
            IoTProperty iotProperty = new IoTProperty(propName.ToLower(), iotDeviceComponentName.ToLower(), type);

            this._iotProperties.Add(iotProperty);
        }

        private void SetIoTPropertyStatusValue<TValue>(string iotDeviceComponentName, string propName, TValue itemValue)
        {
            IoTProperty? item = this._iotProperties.FirstOrDefault(i => i.IoTComponentName == iotDeviceComponentName.ToLower() && i.Name == propName.ToLower() && i.Type == typeof(TValue));
            if (item is not null)
            {
                item.StatusValue = itemValue;
                this.Logger.LogInformation(Lang.IoTClient_SetIoTPropertyStatusValue_SetStatus, this.CurrentSession.SessionId, propName, itemValue);
            }
        }

        private async Task SendIoTMessageAsync<TMessage>(TMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), Lang.IoTClient_SendIoTMessageAsync_MessageNull);
            }
            var mcpMessage = new
            {
                Type = "iot",
                Commands = new List<TMessage> { message }
            };
            string jsonMessage = mcpMessage.ToJson();
            await this.CurrentSession.SendOutter.SendAsync(jsonMessage);
        }

        public override void Dispose()
        {

        }
    }
}
