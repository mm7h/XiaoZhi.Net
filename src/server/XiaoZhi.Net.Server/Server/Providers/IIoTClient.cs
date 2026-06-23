using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Models;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IIoTClient : IProvider<Session>
    {
        void HandleIoTMessage(JsonObject jsonObject);
        /// <summary>执行 IoT 命令，arguments 为从 LLM 调用中提取的参数字典</summary>
        Task<FunctionReturn<string>> ExecuteIoTCommandAsync(string iotDeviceComponentName, string functionName, IReadOnlyDictionary<string, object?> arguments, IReadOnlyList<IoTParameterInfo> parameterInfos, CancellationToken cancellationToken = default);
        FunctionReturn<object?> GetIoTPropertyStatus(string functionName, Type returnValueType);
    }
}
