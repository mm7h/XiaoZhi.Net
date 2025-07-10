using ModelContextProtocol.Protocol;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Server.Providers.MCP
{
    internal abstract class BaseMcpClient : BaseProvider
    {
        private readonly Session _currentSession;
        private readonly object _locker = new object();

        private bool _isReady = false;
        private int _nextId = 1;

        private IDictionary<string, Tool> _mcpTools = new ConcurrentDictionary<string, Tool>();
        private IDictionary<int, TaskCompletionSource<JsonObject>> _callResults = new ConcurrentDictionary<int, TaskCompletionSource<JsonObject>>();



        public BaseMcpClient(Session session, ILogger logger) : base(logger)
        {
            this._currentSession = session;
        }
        public ICollection<Tool> Tools => this._mcpTools.Values;

        public bool IsReady
        {
            get
            {
                lock (this._locker)
                {
                    return this._isReady;
                }
            }
            set
            {
                lock (this._locker)
                {
                    this._isReady = value;
                }
            }
        }

        public int NextId => Interlocked.Increment(ref this._nextId);

        public async Task HandleMcpMessage(JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("result", out var result) && result is not null)
            {
                int msgId = result["id"]?.AsValue().GetValue<int>() ?? 0;


                if (msgId == 1)
                {
                    // mcp initialize id
                    this.Logger.Information("Received MCP Initialize message from client: {sessionId}.", this._currentSession.SessionId);
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
                    this.Logger.Information("Received MCP Initialize message from client: {sessionId}.", this._currentSession.SessionId);

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
                        }

                        string nextCursor = resultObj["nextCursor"]?.GetValue<string>() ?? string.Empty;
                        if (!string.IsNullOrEmpty(nextCursor))
                        {
                            this.Logger.Information("Detected that there are more tools available, nextCursor: {nextCursor}", nextCursor);
                            await this.RequestToolsList(nextCursor);
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

                }
            }
        }

        public async Task RequestToolsList()
        {
            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = "tools/list",
                Id = new RequestId(2)
            };

            this.Logger.Debug("Session {SessionId} request tools list.", _currentSession.SessionId);

            await this.SendMCPMessage(request);
        }

        public async Task RequestToolsList(string cursor)
        {
            var @params = new { cursor };
            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = "tools/list",
                Id = new RequestId(2),
                Params = @params.ToNode()
            };

            this.Logger.Debug("Session {SessionId} request tools list.", this._currentSession.SessionId);

            await this.SendMCPMessage(request);
        }

        public virtual Task SendMCPMessage<TMessage>(TMessage message)
        {
            return Task.CompletedTask;
        }
        public void AddTool(Tool mcpTool)
        {
            lock (this._locker)
            {
                string sanitizedToolName = this.SanitizeToolName(mcpTool.Name);
                if (this._mcpTools.ContainsKey(sanitizedToolName))
                {
                    this.Logger.Warning("Tool with name {ToolName} already exists, skipping.", sanitizedToolName);
                    return;
                }
                this._mcpTools.Add(sanitizedToolName, mcpTool);
            }
        }


        public void AddTools(ICollection<Tool> mcpTools)
        {
            lock (this._locker)
            {
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
        }

        public bool HasTool(string toolName)
        {
            lock (this._locker)
            {
                return this._mcpTools.ContainsKey(toolName);
            }
        }

        public async virtual Task CallMcpToolAsync(string toolName, string args = "{}", int timeout = 30)
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

            try
            {
                //todo: arguments handle
            }
            catch (Exception)
            {

                throw;
            }

            if (this._mcpTools.TryGetValue(toolName, out Tool mcpTool))
            {
                string realToolName = mcpTool.Name;

                var @params = new
                { 
                    Name = realToolName,
                    Arguments = new { }
                };

                JsonRpcRequest request = new JsonRpcRequest
                {
                    JsonRpc = "2.0",
                    Method = "tools/call",
                    Id = new RequestId(toolCallId),
                    Params = @params.ToNode()
                };
            }

            this.Logger.Debug("Session {sessionId} call MCP tool: {toolName}, args: {args}", this._currentSession.SessionId, toolName, "arg");

            try
            {
                // 等待响应，设置超时
                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeout));
                Task completedTask = await Task.WhenAny(resultTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException($"Response timeout after {timeout} seconds");
                }

                JsonObject result = await resultTask;
                this.Logger.Debug("Got the result of MCP tool call from the session {sessionId} successfully, result: {result}", this._currentSession.SessionId, result.ToJsonString());

                //todo: handle the result
            }
            catch (Exception)
            {

                throw;
            }
            finally
            { 
                this.CleanCallResults(toolCallId);
            }
        }
        public virtual Task<JsonObject> RegisterCallResultAsync(int id)
        {
            TaskCompletionSource<JsonObject> tcs = new TaskCompletionSource<JsonObject>();
            this._callResults.TryAdd(id, tcs);
            return tcs.Task;
        }

        public virtual void ResolveCallResult(int id, JsonObject result)
        {
            if (this._callResults.TryGetValue(id, out TaskCompletionSource<JsonObject> tcs) && !tcs.Task.IsCompleted)
            {
                tcs.SetResult(result);
            }
        }

        public virtual void RejectCallResult(int id, string errorMessage)
        {
            if (this._callResults.TryGetValue(id, out TaskCompletionSource<JsonObject> tcs) && !tcs.Task.IsCompleted)
            {
                tcs.SetException(new Exception(errorMessage));
            }
        }

        public virtual bool CleanCallResults(int id)
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
