using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IIoTClient : IProvider
    {
        void HandleIoTMessage(JsonObject jsonObject);
        Task ExecuteIoTCommand(string iotDeviceComponentName, string functionName, IReadOnlyList<KernelParameterMetadata> parameterInfos, KernelArguments arguments);
        object? GetIoTPropertyStatus(string functionName, Type returnValueType);
    }
}
