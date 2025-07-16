using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal abstract class BaseMcpClient : BaseProvider, ISubMcpClient
    {
        private readonly SemaphoreSlim _lockerSlim = new SemaphoreSlim(1, 1);

        private bool _isReady = false;
        private int _nextId = 1;

        private IDictionary<string, Tool> _mcpTools = new ConcurrentDictionary<string, Tool>();
        private IDictionary<int, TaskCompletionSource<JsonObject>> _callResults = new ConcurrentDictionary<int, TaskCompletionSource<JsonObject>>();



        public BaseMcpClient(Session session, ModelSetting mcpSetting, ILogger logger) : base(mcpSetting, logger)
        {
            this.CurrentSession = session;
        }
        public ICollection<Tool> Tools => this._mcpTools.Values;

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

        protected Session CurrentSession { get; }

        public async Task HandleMcpMessageAsync(JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("result", out var result) && result is not null)
            {
                int msgId = result["id"]?.AsValue().GetValue<int>() ?? 0;

                if (this._callResults.ContainsKey(msgId))
                {
                    this.Logger.Debug("Received MCP call result: {result} for message ID {msgId} from session {sessionId}.", result.ToJsonString(), msgId, this.CurrentSession.SessionId);
                    this.ResolveCallResult(msgId, result.AsObject());
                    return;
                }

                if (msgId == 1)
                {
                    // mcp initialize id
                    this.Logger.Information("Received MCP Initialize message from client: {sessionId}.", this.CurrentSession.SessionId);
                    if (result.AsObject().TryGetPropertyValue("serverInfo", out var serverInfo) && serverInfo is not null)
                    {
                        string? name = serverInfo["name"]?.GetValue<string>();
                        string? version = serverInfo["version"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version))
                        {
                            this.Logger.Information("The server info from xiaozhi client MCP: name - {name}, version - {version}", name, version);
                        }
                        else
                        {
                            this.Logger.Warning("Invalid server info received from xiaozhi client MCP.");
                        }
                    }

                    return;
                }
                else if (msgId == 2)
                {
                    // mcp tools list id
                    this.Logger.Information("Received MCP Initialize message from client: {sessionId}.", this.CurrentSession.SessionId);

                    if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("tools", out var toolsNode) && toolsNode is JsonArray toolsArray)
                    {

                        List<Tool>? tools = JsonHelper.Deserialize<List<Tool>>(toolsArray.ToJsonString());

                        if (tools is null)
                        {
                            this.Logger.Warning("Cannot get the MCP tools from client.");
                            return;
                        }

                        this.Logger.Information("Number of tools supported by client devices: {count}", tools.Count);

                        foreach (var tool in tools)
                        {
                            if (tool is null)
                                continue;
                            var inputSchema = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject(),
                                ["required"] = new JsonArray()
                            };

                            if (tool.InputSchema.ValueKind == JsonValueKind.Object)
                            {
                                var schemaObj = tool.InputSchema;
                                if (schemaObj.TryGetProperty("type", out var typeProp))
                                {
                                    inputSchema["type"] = typeProp.GetString() ?? "object";
                                }
                                if (schemaObj.TryGetProperty("properties", out var propertiesProp) && propertiesProp.ValueKind == JsonValueKind.Object)
                                {
                                    inputSchema["properties"] = JsonNode.Parse(propertiesProp.GetRawText()) as JsonObject ?? new JsonObject();
                                }
                                if (schemaObj.TryGetProperty("required", out var requiredProp) && requiredProp.ValueKind == JsonValueKind.Array)
                                {
                                    var filtered = new JsonArray();
                                    foreach (var s in requiredProp.EnumerateArray())
                                    {
                                        if (s.ValueKind == JsonValueKind.String)
                                            filtered.Add(s.GetString());
                                    }
                                    inputSchema["required"] = filtered;
                                }
                            }

                            Tool newTool = new Tool
                            {
                                Name = tool.Name,
                                Description = tool.Description?.Replace(tool.Name, this.SanitizeToolName(tool.Name)),
                                InputSchema = this.ParseJsonElement(System.Text.Encoding.UTF8.GetBytes(inputSchema.ToJsonString()))
                            };

                            this.AddTool(newTool);
                            this.Logger.Information("Tool added: {ToolName}", newTool.Name);
                        }

                        string nextCursor = resultObj["nextCursor"]?.GetValue<string>() ?? string.Empty;
                        if (!string.IsNullOrEmpty(nextCursor))
                        {
                            this.Logger.Information("Detected that there are more tools available, nextCursor: {nextCursor}", nextCursor);
                            await this.RequestToolsListAsync(nextCursor);
                        }
                        else
                        {
                            this.IsReady = true;
                            this.Logger.Information("All tools have been obtained, MCP client is ready.");

                            //// 刷新工具缓存，确保MCP工具被包含在函数列表中
                            //if (conn?.FuncHandler?.ToolManager is not null)
                            //{
                            //    conn.FuncHandler.ToolManager.RefreshTools();
                            //    conn.FuncHandler.CurrentSupportFunctions();
                            //}
                        }
                    }
                    return;
                }
            }
            else if (jsonObject.TryGetPropertyValue("method", out var method) && method is not null)
            {
                this.Logger.Information("Received MCP client request: {method}", method.GetValue<string>());
            }
            else if (jsonObject.TryGetPropertyValue("error", out var error) && error is not null)
            {
                var errorMsg = error["message"]?.GetValue<string>() ?? "未知错误";

                this.Logger.Error("Received MCP error response: {ErrorMsg}", errorMsg);

                if (jsonObject.TryGetPropertyValue("id", out var msgId) && msgId is not null)
                {
                    this.RejectCallResult(msgId.GetValue<int>(), $"Received MCP error response: {errorMsg}");
                }
            }
        }

        protected virtual async Task SendMcpInitializeAsync(string clientName)
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
                    Name = clientName,
                    Version = "1.0.0"
                }
            };
            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = RequestMethods.ToolsList,
                Id = new RequestId(1),
                Params = mcpClientOptions.ToNode()
            };

            this.Logger.Information("Session {sessionId} sending MCP Initialize request.", this.CurrentSession.SessionId);

            await this.SendMCPMessageAsync(request);
        }
        protected virtual async Task SendMcpNotificationAsync(string method)
        {
            var @params = new { };
            JsonRpcNotification request = new JsonRpcNotification
            {
                JsonRpc = "2.0",
                Method = method,
                Params = @params.ToNode()
            };

            this.Logger.Debug("Session {sessionId} sending MCP notification: {method}.", this.CurrentSession.SessionId, method);

            await this.SendMCPMessageAsync(request);
        }

        protected virtual async Task RequestToolsListAsync()
        {
            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = RequestMethods.ToolsList,
                Id = new RequestId(2)
            };

            this.Logger.Debug("Session {SessionId} request tools list.", CurrentSession.SessionId);

            await this.SendMCPMessageAsync(request);
        }

        protected virtual async Task RequestToolsListAsync(string cursor)
        {
            var @params = new { cursor };
            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = RequestMethods.ToolsList,
                Id = new RequestId(2),
                Params = @params.ToNode()
            };

            this.Logger.Debug("Session {SessionId} request tools list with cursor: {cursor}.", this.CurrentSession.SessionId, cursor);

            await this.SendMCPMessageAsync(request);
        }

        protected abstract Task SendMCPMessageAsync<TMessage>(TMessage message);

        protected void AddTool(Tool mcpTool)
        {
            try
            {
                this._lockerSlim.Wait();
                string sanitizedToolName = this.SanitizeToolName(mcpTool.Name);
                if (this._mcpTools.ContainsKey(sanitizedToolName))
                {
                    this.Logger.Warning("Tool with name {ToolName} already exists, skipping.", sanitizedToolName);
                    return;
                }
                this._mcpTools.Add(sanitizedToolName, mcpTool);
            }
            finally
            {
                this._lockerSlim.Release();
            }
        }


        protected void AddTools(ICollection<Tool> mcpTools)
        {
            try
            {
                this._lockerSlim.Wait();
                foreach (var tool in mcpTools)
                {
                    string sanitizedToolName = this.SanitizeToolName(tool.Name);
                    if (this._mcpTools.ContainsKey(sanitizedToolName))
                    {
                        this.Logger.Warning("Tool with name {ToolName} already exists, skipping.", sanitizedToolName);
                        continue;
                    }
                    this._mcpTools[sanitizedToolName] = tool;
                }
            }
            finally
            {
                this._lockerSlim.Release();
            }
        }

        protected bool HasTool(string toolName)
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

        protected async virtual Task<string> CallMcpToolAsync(string toolName, string args = "{}", int timeout = 30)
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

            JsonObject arguments;
            try
            {
                arguments = JsonNode.Parse(string.IsNullOrEmpty(args.Trim()) ? "{}" : args)?.AsObject() ?? new JsonObject();
            }
            catch (JsonException)
            {
                try
                {
                    var jsonObjects = Regex.Matches(args, @"\{[^{}]*\}")
                        .Cast<Match>()
                        .Select(m => m.Value)
                        .ToList();

                    if (jsonObjects.Count > 1)
                    {
                        var mergedDict = new Dictionary<string, object>();
                        foreach (var jsonStr in jsonObjects)
                        {
                            try
                            {
                                var obj = JsonHelper.Deserialize<Dictionary<string, object>>(jsonStr);
                                if (obj != null)
                                {
                                    foreach (var kv in obj)
                                    {
                                        mergedDict[kv.Key] = kv.Value;
                                    }
                                }
                            }
                            catch (JsonException)
                            {
                                continue;
                            }
                        }
                        if (mergedDict.Count > 0)
                        {
                            // 将 mergedDict 转换为 JsonObject 并赋值给 arguments
                            arguments = new JsonObject();
                            foreach (var kv in mergedDict)
                            {
                                // 其他类型先序列化为 JSON 字符串再解析为 JsonNode
                                var json = JsonHelper.Serialize(kv.Value);
                                arguments[kv.Key] = JsonNode.Parse(json);
                            }
                        }
                        else
                        {
                            throw new ArgumentException($"Unable to parse any valid JSON object: {args}");
                        }
                    }
                    else
                    {
                        throw new ArgumentException($"Failed to parse JSON: {args}");
                    }
                }
                catch (Exception e)
                {
                    this.Logger.Error(e, "Failed to parse tool arguments: {args}", args);
                    throw e;
                }
            }

            if (this._mcpTools.TryGetValue(toolName, out Tool mcpTool))
            {
                string realToolName = mcpTool.Name;

                var @params = new
                {
                    Name = realToolName,
                    Arguments = arguments
                };

                JsonRpcRequest request = new JsonRpcRequest
                {
                    JsonRpc = "2.0",
                    Id = new RequestId(toolCallId),
                    Method = RequestMethods.ToolsCall,
                    Params = @params.ToNode()
                };
                this.Logger.Debug("Session {sessionId} call MCP tool: {toolName}, args: {args}", this.CurrentSession.SessionId, realToolName, args);
                await this.SendMCPMessageAsync(request);
            }

            try
            {
                // 等待响应，设置超时
                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeout));
                Task completedTask = await Task.WhenAny(resultTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    this.Logger.Error("Response timeout after {timeout} seconds for tool call: {toolName}", timeout, toolName);
                    throw new TimeoutException($"Response timeout after {timeout} seconds");
                }

                JsonObject rawResult = await resultTask;
                this.Logger.Debug("Got the result of MCP tool call from the session {sessionId} successfully, result: {result}", this.CurrentSession.SessionId, rawResult.ToJsonString());

                if (rawResult.TryGetPropertyValue("isError", out var isErrorNode) && isErrorNode is not null && isErrorNode.GetValue<bool>())
                {
                    var errorMsg = rawResult?["error"]?.GetValue<string>() ?? "The tool call returned an error, but no specific error information was provided.";
                    throw new Exception($"Tool call error: {errorMsg}");
                }

                if (rawResult.TryGetPropertyValue("content", out var contentNode) && contentNode is not null && contentNode is JsonArray content && content.Any())
                {
                    var firstItem = content.First();
                    if (firstItem is JsonObject first && first.TryGetPropertyValue("text", out var textNode) && textNode is not null)
                    {
                        return textNode.GetValue<string>();
                    }
                }

                return JsonHelper.Serialize(rawResult);
            }
            catch (TimeoutException timeoutException)
            {
                this.CleanCallResults(toolCallId);
                this.Logger.Error(timeoutException, "Timeout while waiting for MCP tool call response: {toolName}, args: {args}", toolName, args);
                throw timeoutException;
            }
            catch (Exception e)
            {
                this.CleanCallResults(toolCallId);
                this.Logger.Error(e, "Failed to call MCP tool: {toolName}, args: {args}", toolName, args);
                throw e;
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
            if (this._callResults.TryGetValue(id, out TaskCompletionSource<JsonObject> tcs) && !tcs.Task.IsCompleted)
            {
                tcs.SetResult(result);
            }
        }

        protected virtual void RejectCallResult(int id, string errorMessage)
        {
            if (this._callResults.TryGetValue(id, out TaskCompletionSource<JsonObject> tcs) && !tcs.Task.IsCompleted)
            {
                tcs.SetException(new Exception(errorMessage));
            }
        }

        protected virtual bool CleanCallResults(int id)
        {
            return this._callResults.Remove(id);
        }

        private JsonElement ParseJsonElement(ReadOnlySpan<byte> utf8Json)
        {
            Utf8JsonReader reader = new(utf8Json);
            return JsonElement.ParseValue(ref reader);
        }

        private string SanitizeToolName(string name)
        {
            return Regex.Replace(name, @"[^a-zA-Z0-9_\\-\u4e00-\u9fff]", "_");
        }
    }
}
