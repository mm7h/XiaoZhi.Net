using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal abstract class BaseMcpClient<TLogger> : BaseProvider<TLogger, MCPClientBuildConfig>, ISubMcpClient
    {
        private readonly SemaphoreSlim _lockerSlim = new SemaphoreSlim(1, 1);

        private bool _isReady = false;
        private int _nextId = 1;

        private IDictionary<string, AIFunction> _mcpTools = new ConcurrentDictionary<string, AIFunction>();
    private IDictionary<string, FunctionToolRegistration> _mcpToolRegistrations = new ConcurrentDictionary<string, FunctionToolRegistration>();
        private IDictionary<int, TaskCompletionSource<JsonObject>> _callResults = new ConcurrentDictionary<int, TaskCompletionSource<JsonObject>>();

        public BaseMcpClient(ILogger<TLogger> logger) : base(logger)
        {

        }
        public ICollection<AIFunction> Functions => this._mcpTools.Values;

        public bool IsReady
        {
            get
            {
                try
                {
                    this._lockerSlim.Wait();
                    return this._isReady;
                }
                finally
                {
                    this._lockerSlim.Release();
                }
            }
            set
            {
                try
                {
                    this._lockerSlim.Wait();
                    this._isReady = value;
                }
                finally
                {
                    this._lockerSlim.Release();
                }
            }
        }

        public int NextId => Interlocked.Increment(ref this._nextId);

        protected Session CurrentSession { get; set; } = null!;
        protected IDictionary<string, object?> AdditionalMetadataDic { get; set; } = null!;

        public async virtual Task HandleMcpMessageAsync(JsonObject payloadObj)
        {
            if (payloadObj.TryGetPropertyValue("result", out var result) && result is not null)
            {
                int msgId = payloadObj["id"]?.AsValue().GetValue<int>() ?? 0;

                if (this._callResults.ContainsKey(msgId))
                {
                    this.Logger.LogDebug(Lang.BaseMcpClient_HandleMcpMessageAsync_CallResult, result.ToJsonString(), msgId, this.CurrentSession.DeviceId);
                    this.ResolveCallResult(msgId, result.AsObject());
                    return;
                }

                if (msgId == 1)
                {
                    // mcp initialize id
                    this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_InitMessage, this.CurrentSession.DeviceId);
                    if (result.AsObject().TryGetPropertyValue("serverInfo", out var serverInfo) && serverInfo is not null)
                    {
                        string? name = serverInfo["name"]?.GetValue<string>();
                        string? version = serverInfo["version"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version))
                        {
                            this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ServerInfo, name, version);
                        }
                        else
                        {
                            this.Logger.LogWarning(Lang.BaseMcpClient_HandleMcpMessageAsync_InvalidServerInfo);
                        }
                    }

                    return;
                }
                else if (msgId == 2)
                {
                    // mcp tools list id
                    this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ToolListMessage, this.CurrentSession.DeviceId);

                    if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("tools", out var toolsNode) && toolsNode is JsonArray toolsJson)
                    {

                        foreach (JsonNode? item in toolsJson)
                        {
                            if (item is not JsonObject itemObj)
                                continue;

                            string toolName = item["name"]?.GetValue<string>() ?? "";
                            string toolDescription = item["description"]?.GetValue<string>() ?? "";

                            JsonObject inputSchema = item["inputSchema"]?.AsObject() ?? new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject(),
                                ["required"] = new JsonArray()
                            };

                            JsonObject properties = inputSchema["properties"] as JsonObject ?? new JsonObject();
                            JsonArray requiredProperties = inputSchema["required"] as JsonArray ?? new JsonArray();

                            List<string> paramDescs = new List<string>();
                            foreach (var property in properties)
                            {
                                if (property.Value is JsonObject propObj)
                                {
                                    string propName = property.Key;
                                    string propDescription = propObj["description"]?.GetValue<string>() ?? string.Empty;
                                    bool isRequired = requiredProperties.Any(r => r?.GetValue<string>() == propName);
                                    paramDescs.Add($"- {propName}{(isRequired ? " (required)" : "(optional)")}: {propDescription}");
                                }
                            }

                            string capturedToolName = toolName;
                            BaseMcpClient<TLogger> capturedClient = this;
                            string fullDescription = paramDescs.Count > 0
                                ? $"{toolDescription}\n参数格式为 JSON 对象，字段如下:\n{string.Join("\n", paramDescs)}"
                                : toolDescription;

                            AIFunction toolFunc = AIFunctionFactory.Create(
                                async (string argumentsJson, CancellationToken ct) =>
                                {
                                    var argDict = string.IsNullOrEmpty(argumentsJson)
                                        ? new Dictionary<string, object?>()
                                        : JsonHelper.Deserialize<Dictionary<string, object?>>(argumentsJson) ?? new Dictionary<string, object?>();
                                    return await capturedClient.CallMcpToolAsync(capturedToolName, argDict);
                                },
                                this.SanitizeToolName(toolName),
                                fullDescription,
                                JsonHelper.OPTIONS);

                            string inputJsonSchema = inputSchema.ToJsonString(JsonHelper.OPTIONS);
                            FunctionMetadata metadata = toolFunc.ToFunctionMetadata(inputJsonSchema, toolDescription);
                            FunctionToolRegistration registration = new FunctionToolRegistration(toolFunc, metadata, ToolAction.Continue);

                            this.AddTool(toolName, registration);
                            this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ToolAdded, toolName);
                        }

                        string nextCursor = resultObj["nextCursor"]?.GetValue<string>() ?? string.Empty;
                        if (!string.IsNullOrEmpty(nextCursor))
                        {
                            this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_MoreTools, nextCursor);
                            await this.RequestToolsListAsync(nextCursor);
                        }
                        else
                        {
                            this.IsReady = true;
                            // 将所有 AIFunction 注册到 session 共享工具列表
                            foreach (FunctionToolRegistration registration in this._mcpToolRegistrations.Values)
                            {
                                this.CurrentSession.PrivateProvider.AddFunctionToolRegistration(registration);
                            }
                            this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ClientReady);
                            this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ToolCount, this._mcpToolRegistrations.Values.Count);
                        }

                        return;
                    }
                    else
                    {
                        this.Logger.LogWarning(Lang.BaseMcpClient_HandleMcpMessageAsync_GetToolsFailed);
                        return;
                    }
                }
            }
            else if (payloadObj.TryGetPropertyValue("method", out var method) && method is not null)
            {
                this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ClientRequest, method.GetValue<string>());
            }
            else if (payloadObj.TryGetPropertyValue("error", out var error) && error is not null)
            {
                var errorMsg = error["message"]?.GetValue<string>() ?? "未知错误";

                this.Logger.LogError(Lang.BaseMcpClient_HandleMcpMessageAsync_ErrorResponse, errorMsg);

                if (payloadObj.TryGetPropertyValue("id", out var msgId) && msgId is not null)
                {
                    this.RejectCallResult(msgId.GetValue<int>(), string.Format(Lang.BaseMcpClient_HandleMcpMessageAsync_ErrorResponse_Exception, errorMsg));
                }
            }
        }

        public virtual async Task SendMcpInitializeAsync()
        {
            McpClientOptions mcpClientOptions = new McpClientOptions
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new ClientCapabilities
                {
                    Roots = new RootsCapability { ListChanged = true },
                    Sampling = new SamplingCapability { }
                },
                ClientInfo = new Implementation
                {
                    Name = this.ModelName,
                    Version = "1.0.0"
                }
            };
            JsonRpcRequest request = new JsonRpcRequest
            {
                Method = RequestMethods.Initialize,
                Id = new RequestId(1),
                Params = mcpClientOptions.ToNode()
            };

            this.Logger.LogInformation(Lang.BaseMcpClient_SendMcpInitializeAsync_SendingInit, this.CurrentSession.DeviceId);

            await this.SendMCPMessageAsync(request);
        }
        public virtual async Task SendMcpNotificationAsync(string method)
        {
            var @params = new { };
            JsonRpcNotification request = new JsonRpcNotification
            {
                Method = method,
                Params = @params.ToNode()
            };

            this.Logger.LogDebug(Lang.BaseMcpClient_SendMcpNotificationAsync_SendingNotification, this.CurrentSession.DeviceId, method);

            await this.SendMCPMessageAsync(request);
        }

        public virtual async Task RequestToolsListAsync()
        {
            JsonRpcRequest request = new JsonRpcRequest
            {
                Method = RequestMethods.ToolsList,
                Id = new RequestId(2)
            };

            this.Logger.LogDebug(Lang.BaseMcpClient_RequestToolsListAsync_RequestTools, this.CurrentSession.DeviceId);

            await this.SendMCPMessageAsync(request);
        }

        public virtual async Task RequestToolsListAsync(string cursor)
        {
            var @params = new { cursor };
            JsonRpcRequest request = new JsonRpcRequest
            {
                Method = RequestMethods.ToolsList,
                Id = new RequestId(2),
                Params = @params.ToNode()
            };

            this.Logger.LogDebug(Lang.BaseMcpClient_RequestToolsListAsync_RequestToolsWithCursor, this.CurrentSession.DeviceId, cursor);

            await this.SendMCPMessageAsync(request);
        }



        public bool HasTool(string toolName)
        {
            try
            {
                this._lockerSlim.Wait();
                return this._mcpTools.ContainsKey(toolName);
            }
            finally
            {
                this._lockerSlim.Release();
            }
        }

        public async virtual Task<FunctionReturn<string>> CallMcpToolAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, int timeout = 30)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                throw new Exception("Invalid and empty tool name.");
            }
            if (!this.IsReady)
            {
                throw new Exception("The client is not ready yet.");
            }
            if (!this.HasTool(toolName))
            {
                throw new Exception($"The tool ({toolName}) is not registered yet.");
            }
            int toolCallId = this.NextId;
            Task<JsonObject> resultTask = this.RegisterCallResultAsync(toolCallId);

            string argJson = System.Text.Json.JsonSerializer.Serialize(arguments);

            if (this._mcpTools.TryGetValue(toolName, out AIFunction? mcpTool))
            {
                string realToolName = mcpTool.Name;

                var @params = new
                {
                    Name = realToolName,
                    Arguments = arguments
                };

                JsonRpcRequest request = new JsonRpcRequest
                {
                    Id = new RequestId(toolCallId),
                    Method = RequestMethods.ToolsCall,
                    Params = @params.ToNode()
                };
                this.Logger.LogDebug(Lang.BaseMcpClient_CallMcpToolAsync_CallTool, this.CurrentSession.DeviceId, realToolName, argJson);
                await this.SendMCPMessageAsync(request);
            }

            try
            {
                // 等待响应，设置超时
                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeout));
                Task completedTask = await Task.WhenAny(resultTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    this.Logger.LogError(Lang.BaseMcpClient_CallMcpToolAsync_Timeout, timeout, toolName);
                    throw new TimeoutException(string.Format(Lang.BaseMcpClient_CallMcpToolAsync_TimeoutEx, timeout));
                }

                JsonObject rawResult = await resultTask;
                this.Logger.LogDebug(Lang.BaseMcpClient_CallMcpToolAsync_CallSuccess, this.CurrentSession.DeviceId, rawResult.ToJsonString());

                if (rawResult.TryGetPropertyValue("isError", out var isErrorNode) && isErrorNode is not null && isErrorNode.GetValue<bool>())
                {
                    var errorMsg = rawResult?["error"]?.GetValue<string>() ?? "The tool call returned an error, but no specific error information was provided.";
                    throw new Exception(string.Format(Lang.BaseMcpClient_CallMcpToolAsync_ToolError, errorMsg));
                }

                if (rawResult.TryGetPropertyValue("content", out var contentNode) && contentNode is not null && contentNode is JsonArray content && content.Any())
                {
                    var firstItem = content.First();
                    if (firstItem is JsonObject first && first.TryGetPropertyValue("text", out var textNode) && textNode is not null)
                    {
                        string text = textNode.GetValue<string>();
                        return new FunctionReturn<string>
                        {
                            Next = ToolAction.Continue,
                            Result = text,
                            Response = text
                        };
                    }
                }

                string serialized = JsonHelper.Serialize(rawResult);
                return new FunctionReturn<string>
                {
                    Next = ToolAction.Continue,
                    Result = serialized,
                    Response = serialized
                };
            }
            catch (TimeoutException timeoutException)
            {
                this.CleanCallResults(toolCallId);
                this.Logger.LogError(timeoutException, Lang.BaseMcpClient_CallMcpToolAsync_WaitTimeout, toolName, argJson);
                throw;
            }
            catch (Exception e)
            {
                this.CleanCallResults(toolCallId);
                this.Logger.LogError(e, Lang.BaseMcpClient_CallMcpToolAsync_CallFailed, toolName, argJson);
                throw;
            }
        }

        protected abstract Task SendMCPMessageAsync<TMessage>(TMessage message);

        protected void AddTool(string toolName, FunctionToolRegistration toolRegistration)
        {
            try
            {
                this._lockerSlim.Wait();
                if (this._mcpTools.ContainsKey(toolName))
                {
                    this.Logger.LogWarning(Lang.BaseMcpClient_AddTool_ToolExists, toolName);
                    return;
                }
                this._mcpTools.Add(toolName, toolRegistration.Function);
                this._mcpToolRegistrations.Add(toolName, toolRegistration);
            }
            finally
            {
                this._lockerSlim.Release();
            }
        }

        protected virtual Task<JsonObject> RegisterCallResultAsync(int id)
        {
            TaskCompletionSource<JsonObject> tcs = new TaskCompletionSource<JsonObject>();
            this._callResults.TryAdd(id, tcs);
            return tcs.Task;
        }

        protected virtual void ResolveCallResult(int id, JsonObject result)
        {
            if (this._callResults.TryGetValue(id, out TaskCompletionSource<JsonObject>? tcs) && !tcs.Task.IsCompleted)
            {
                tcs.SetResult(result);
            }
        }

        protected virtual void RejectCallResult(int id, string errorMessage)
        {
            if (this._callResults.TryGetValue(id, out TaskCompletionSource<JsonObject>? tcs) && !tcs.Task.IsCompleted)
            {
                tcs.SetException(new Exception(errorMessage));
            }
        }

        protected virtual bool CleanCallResults(int id)
        {
            return this._callResults.Remove(id);
        }

        protected void InitSession(MCPClientBuildConfig config)
        {
            this.CurrentSession = config.Session;
            this.AdditionalMetadataDic = new Dictionary<string, object?>
            {
                { "session_id", this.CurrentSession.SessionId }
            };
        }

        private string SanitizeToolName(string name)
        {
            return Regex.Replace(name, @"[^a-zA-Z0-9_\\-\u4e00-\u9fff]", "_");
        }


    }
}
