using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IIoTClient : IProvider<Session>
    {
        void HandleIoTMessage(JsonObject jsonObject);
        Task ExecuteIoTCommand(string iotDeviceComponentName, string functionName, IReadOnlyList<KernelParameterMetadata> parameterInfos, KernelArguments arguments);
        object? GetIoTPropertyStatus(string functionName, Type returnValueType);
    }
}
