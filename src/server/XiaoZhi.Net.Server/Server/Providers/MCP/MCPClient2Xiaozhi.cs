using ModelContextProtocol.Protocol;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal class MCPClient2Xiaozhi
    {
        private readonly Session _currentSession;
        private readonly ILogger _logger;
        private readonly object _locker = new object();

        private bool _isReady = false;
        private List<Tool> _mcpTools = new List<Tool>();

        public MCPClient2Xiaozhi(Session session, ILogger logger)
        {
            this._currentSession = session;
            this._logger = logger;
        }

        public ReadOnlyCollection<Tool> Tools => _mcpTools.AsReadOnly();

        // 将 IsReady 属性改为同步属性，并用 lock 保护
        public bool IsReady
        {
            get
            {
                lock (_locker)
                {
                    return _isReady;
                }
            }
            set
            {
                lock (_locker)
                {
                    _isReady = value;
                }
            }
        }
        public async Task Initialize()
        {
            //vision


            var @params = new
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new
                {
                    Roots = new { ListChanged = true },
                    Sampling = new { },
                    //vision,
                },
                ClientInfo = new
                {
                    Name = "XiaoZhi.Net.Client",
                    Version = Assembly.GetEntryAssembly().GetName().Version.ToString(),
                }
            };

            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = "initialize",
                Id = new RequestId(1),
                Params = @params.ToNode()
            };

            this._logger.Debug("Session {SessionId} send the initialize message.", _currentSession.SessionId);

            await SendMCPMessage(request);
        }

        public async Task RequestToolsList()
        {
            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = "tools/list",
                Id = new RequestId(2)
            };

            this._logger.Debug("Session {SessionId} request tools list.", _currentSession.SessionId);

            await SendMCPMessage(request);
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

            this._logger.Debug("Session {SessionId} request tools list.", this._currentSession.SessionId);

            await SendMCPMessage(request);
        }

        public async Task HandleMcpMessage(JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("result", out var result) && result is not null)
            {
                int msgId = result["id"]?.AsValue().GetValue<int>() ?? 0;


                if (msgId == 1)
                {
                    // mcp initialize id
                    this._logger.Information("Received MCP Initialize message from client: {sessionId}.", this._currentSession.SessionId);
                    if (result.AsObject().TryGetPropertyValue("serverInfo", out var serverInfo) && serverInfo is not null)
                    {
                        string? name = serverInfo["name"]?.GetValue<string>();
                        string? version = serverInfo["version"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version))
                        {
                            this._logger.Information("The server info from xiaozhi client MCP: name - {name}, version - {version}", name, version);
                        }
                        else
                        {
                            this._logger.Warning("Invalid server info received from xiaozhi client MCP.");
                        }
                    }

                    return;
                }
                else if (msgId == 2)
                {
                    // mcp tools list id
                    this._logger.Information("Received MCP Initialize message from client: {sessionId}.", this._currentSession.SessionId);

                    if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("tools", out var toolsNode) && toolsNode is JsonArray toolsArray)
                    {

                        List<Tool>? tools = JsonHelper.Deserialize<List<Tool>>(toolsArray.ToJsonString());

                        if (tools is null)
                        {
                            this._logger.Warning("Cannot get the MCP tools from client.");
                            return;
                        }

                        this._logger.Information("Number of tools supported by client devices: {count}", tools.Count);

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
                            this._logger.Information("Detected that there are more tools available, nextCursor: {nextCursor}", nextCursor);
                            await this.RequestToolsList(nextCursor);
                        }
                        else
                        {
                            this.IsReady = true;
                            this._logger.Information("All tools have been obtained, MCP client is ready.");

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
                this._logger.Information("Received MCP client request: {method}", method.GetValue<string>());
            }
            else if (jsonObject.TryGetPropertyValue("error", out var error) && error is not null)
            {
                var errorMsg = error["message"]?.GetValue<string>() ?? "未知错误";

                this._logger.Error("Received MCP error response: {ErrorMsg}", errorMsg);

                if (jsonObject.TryGetPropertyValue("id", out var msgId) && msgId is not null)
                {

                }
            }
        }

        public async Task SendMCPMessage<TMessage>(TMessage message)
        {
            if (message is null)
            {
                this._logger.Error("Cannot send null message to MCP.");
                return;
            }
            if (!_currentSession.IsSupportMCP)
            {
                this._logger.Warning("Session {SessionId} does not support MCP, cannot send message.", _currentSession.SessionId);
                return;
            }

            var mcpMessage = new
            {
                Type = "mcp",
                payload = message
            };

            await this._currentSession.SendOutter.SendAsync(JsonHelper.Serialize(mcpMessage));
        }





        public void AddTool(Tool mcpTool)
        {
            lock (_locker)
            {
                _mcpTools.Add(mcpTool);
            }
        }


        public void AddTools(List<Tool> mcpTools)
        {
            lock (_locker)
            {
                _mcpTools.AddRange(mcpTools);
            }
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
