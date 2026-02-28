using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Models;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.IoT
{
    internal class IoTClient : BaseProvider<IoTClient, Session>, IIoTClient
    {
        private readonly Action _tempMethod = () => { };
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

        public async Task ExecuteIoTCommand(string iotDeviceComponentName, string functionName, IReadOnlyList<KernelParameterMetadata> parameterInfos, KernelArguments arguments)
        {
            IDictionary<string, object?> resultArgs = new Dictionary<string, object?>(arguments.Count);
            foreach (var argument in arguments)
            {
                KernelParameterMetadata? metadata = parameterInfos.FirstOrDefault(p => string.Equals(p.Name, argument.Key, StringComparison.OrdinalIgnoreCase));
                if (metadata is not null)
                {
                    object? val = IoTTypeMappingHelper.ConvertValue(argument.Value, metadata.ParameterType);
                    resultArgs.Add(argument.Key, val);

                    this.UpdateIoTPropertyStatus(iotDeviceComponentName, metadata.Name, val, metadata.ParameterType);
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
        }

        public object? GetIoTPropertyStatus(string functionName, Type returnValueType)
        {
            List<string> splitedItems = functionName.Split("_", StringSplitOptions.RemoveEmptyEntries).ToList();
            string iotDeviceComponentName = splitedItems[1];
            string propName = splitedItems[2];
            IoTProperty? property = this._iotProperties.FirstOrDefault(i => i.IoTComponentName == iotDeviceComponentName && i.Name == propName && i.Type == returnValueType);
            return property?.StatusValue ?? null;
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
            if (this.CurrentSession.PrivateProvider.Kernel is null)
            {
                this.Logger.LogError(Lang.IoTClient_RegisterIoTTools_KernelNull, this.CurrentSession.DeviceId);
                return;
            }
            int index = 1;
            foreach (JsonNode? descriptor in descriptors)
            {
                if (descriptor is null)
                {
                    continue;
                }

                string iotDeviceComponentName = descriptor["name"]?.GetValue<string>() ?? "";

                if (string.IsNullOrEmpty(iotDeviceComponentName))
                {
                    continue;
                }

                string deviceDescription = descriptor["description"]?.GetValue<string>() ?? "";

                string pluginName = $"{this.ProviderType}_{iotDeviceComponentName}_{index++}";

                List<KernelFunction> deviceFunctions = new List<KernelFunction>();

                JsonObject properties = descriptor["properties"] as JsonObject ?? new JsonObject();
                foreach (var property in properties)
                {
                    if (property.Value is JsonObject propObj)
                    {
                        string propName = property.Key;
                        string propDescription = propObj["description"]?.GetValue<string>() ?? string.Empty;
                        string propType = propObj["type"]?.GetValue<string>() ?? "string";

                        IDictionary<string, object?> propertyDic = new Dictionary<string, object?>
                        {
                            { "session_id", this.CurrentSession.SessionId },
                            { "iot_device_component_name", iotDeviceComponentName }
                        };

                        KernelFunctionFromMethodOptions propertyFunctionOption = new KernelFunctionFromMethodOptions
                        {
                            FunctionName = $"get_{iotDeviceComponentName.ToLower()}_{propName.ToLower()}",
                            Description = string.Format(Lang.IoTClient_RegisterIoTTools_FunctionDescription, propDescription),
                            AdditionalMetadata = new ReadOnlyDictionary<string, object?>(propertyDic),
                            ReturnParameter = new KernelReturnParameterMetadata { ParameterType = IoTTypeMappingHelper.GetIoTType(propType), Schema = KernelJsonSchema.Parse(propObj.ToJsonString()) }
                        };
                        KernelFunction propertyFunction = KernelFunctionFactory.CreateFromMethod(this._tempMethod, propertyFunctionOption);

                        deviceFunctions.Add(propertyFunction);

                        this.RegisterIoTProperties(iotDeviceComponentName, propName, propObj);
                    }
                }

                JsonObject methods = descriptor["methods"] as JsonObject ?? new JsonObject();
                foreach (var method in methods)
                {
                    if (method.Value is JsonObject methodObj)
                    {
                        string methodName = method.Key;
                        string methodDescription = methodObj["description"]?.GetValue<string>() ?? string.Empty;

                        List<KernelParameterMetadata> methodParameters = new List<KernelParameterMetadata>();
                        JsonObject parameters = methodObj["parameters"] as JsonObject ?? new JsonObject();
                        foreach (var parameter in parameters)
                        {
                            if (parameter.Value is JsonObject paramObj)
                            {
                                string paramName = parameter.Key;
                                string paramDescription = paramObj["description"]?.GetValue<string>() ?? string.Empty;
                                string propType = paramObj["type"]?.GetValue<string>() ?? "string";

                                KernelParameterMetadata methodParameter = new KernelParameterMetadata(paramName)
                                {
                                    Description = paramDescription,
                                    IsRequired = true,
                                    ParameterType = IoTTypeMappingHelper.GetIoTType(propType),
                                    Schema = KernelJsonSchema.Parse(paramObj.ToJsonString())
                                };
                                methodParameters.Add(methodParameter);
                            }
                        }

                        IDictionary<string, object?> methodDic = new Dictionary<string, object?>
                        {
                            { "session_id", this.CurrentSession.SessionId },
                            { "iot_device_component_name", iotDeviceComponentName }
                        };

                        KernelFunctionFromMethodOptions methodFunctionOption = new KernelFunctionFromMethodOptions
                        {
                            FunctionName = methodName,
                            Description = methodDescription,
                            Parameters = methodParameters,
                            AdditionalMetadata = new ReadOnlyDictionary<string, object?>(methodDic)
                        };
                        KernelFunction methodFunction = KernelFunctionFactory.CreateFromMethod(_tempMethod, methodFunctionOption);

                        deviceFunctions.Add(methodFunction);
                    }
                }

                this.CurrentSession.PrivateProvider.Kernel.ImportPluginFromFunctions(pluginName, string.Format(Lang.IoTClient_RegisterIoTTools_PluginDescription, !string.IsNullOrEmpty(iotDeviceComponentName) ? iotDeviceComponentName : deviceDescription), deviceFunctions);
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

                if (string.IsNullOrEmpty(iotDeviceComponentName))
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
