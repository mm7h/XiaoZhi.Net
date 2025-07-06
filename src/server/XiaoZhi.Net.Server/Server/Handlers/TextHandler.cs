using ModelContextProtocol.Protocol;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class TextHandler : BaseHandler, IOutHandler<string>
    {
        public TextHandler(XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
        }
        public event Action<Session>? OnManualStop;
        public override string HandlerName => nameof(TextHandler);
        public ISendOutter SendOutter { get; set; } = null!;
        public ChannelWriter<Workflow<string>> NextWriter { get; set; } = null!;

        public async void Handle(string data)
        {
            JsonNode? jsonObject = JsonNode.Parse(data);

            // 判断是否是整数
            if (jsonObject is JsonValue jsonValue && jsonValue.TryGetValue(out int intValue))
            {
                await this.SendOutter.SendAsync(intValue.ToString());
                return;
            }

            this.Logger.Debug("Received text from client: {jsonText}", jsonObject?.ToJsonString());

            if (jsonObject is JsonObject jsonObj)
            {
                string? type = jsonObj["type"]?.GetValue<string>()?.ToLower();
                if (string.IsNullOrEmpty(type))
                {
                    this.Logger.Error("Invalid type for text message handle.");
                    return;
                }

                switch (type)
                {
                    case "hello":
                        this.HandleHelloMessage(jsonObj);
                        break;
                    case "abort":
                        await this.HandleAbortMessage();
                        break;
                    case "listen":
                        this.HandleListen(jsonObj);
                        break;
                    case "iot":
                        this.HandleIotDescriptors();
                        break;
                    case "mcp":
                        _ = Task.Run(() =>
                        {
                            this.HandleMcp(jsonObj);
                        });

                        break;
                }
            }
        }

        private void HandleHelloMessage(JsonObject jsonObj)
        {
            Session session = this.SendOutter.GetSession();

            AudioParams defaultAudioParams = new AudioParams(this.Config.AudioSetting.SampleRate, this.Config.AudioSetting.Channels, this.Config.AudioSetting.FrameDuration);
            HelloMessage defultHelloMessage = new HelloMessage(this.SendOutter.SessionId, this.Config.ServerProtocol.GetDescription().ToLower(), defaultAudioParams);

            if (jsonObj.TryGetPropertyValue("audio_params", out var audioParams) && audioParams != null)
            {
                JsonObject audioParamsObj = audioParams.AsObject();
                if (audioParamsObj.TryGetPropertyValue("format", out var format) && format != null)
                {
                    string formatValue = format.GetValue<string>();
                    if (!string.IsNullOrEmpty(formatValue))
                    {
                        session.AudioFormat = formatValue;
                    }
                }
            }

            if (jsonObj.TryGetPropertyValue("features", out var features) && features != null)
            {
                JsonObject featuresObj = features.AsObject();
                if (featuresObj.TryGetPropertyValue("mcp", out var mcp) && mcp != null)
                {
                    bool isSupportMCP = mcp.GetValue<bool>();
                    if (isSupportMCP)
                    {
                        session.IsSupportMCP = true;
                    }
                }
            }
            this.SendOutter.SendAsync(JsonHelper.Serialize(defultHelloMessage));
        }

        private async Task HandleAbortMessage()
        {
            Session session = this.SendOutter.GetSession();
            this.Logger.Information("Abort message received");
            await this.SendOutter.SendAbortMessageAsync();
            session.Abort();
            this.Logger.Information("Abort message received-end, cancelled the tasks.");
        }

        private async void HandleListen(JsonObject jsonObject)
        {
            Session session = this.SendOutter.GetSession();
            string? mode = jsonObject["mode"]?.GetValue<string>()?.ToLower();
            if (!string.IsNullOrEmpty(mode))
            {
                session.SetListenMode(mode);
                this.Logger.Information("Client voice listening mode setting is: {mode}", mode);
            }

            string? state = jsonObject["state"]?.GetValue<string>()?.ToLower();
            if (!string.IsNullOrEmpty(state))
            {
                if (state == "start")
                {
                    session.ManualStart();
                }
                else if (state == "stop")
                {
                    session.ManualStop();
                    if (session.CheckAsrData())
                    {
                        this.OnManualStop?.Invoke(session);
                    }
                }
                else if (state == "detect")
                {
                    // 用于客户端向服务器告知检测到唤醒词
                    string? text = jsonObject["text"]?.GetValue<string>()?.ToLower();
                    if (!string.IsNullOrEmpty(text))
                    {
                        // startToChat
                        await this.NextWriter!.WriteAsync(new Workflow<string>(session, text));
                    }
                }
            }
        }

        private void HandleIotDescriptors()
        {

        }

        private async void HandleMcp(JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("result", out var result) && result != null)
            {
                int msgId = result["id"]?.AsValue().GetValue<int>() ?? 0;


                if (msgId == 1)
                {
                    // mcp initialize id
                    this.Logger.Information("Received MCP Initialize message from client: {sessionId}.", this.SendOutter.SessionId);
                    if (result.AsObject().TryGetPropertyValue("serverInfo", out var serverInfo) && serverInfo != null)
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
                    this.Logger.Information("Received MCP Initialize message from client: {sessionId}.", this.SendOutter.SessionId);

                    if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("tools", out var toolsNode) && toolsNode is JsonArray toolsArray)
                    {
                        Session session = this.SendOutter.GetSession();

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

                            session.MCPClient.AddTool(newTool);
                        }

                        string nextCursor = resultObj["nextCursor"]?.GetValue<string>() ?? string.Empty;
                        if (!string.IsNullOrEmpty(nextCursor))
                        {
                            this.Logger.Information("Detected that there are more tools available, nextCursor: {nextCursor}", nextCursor);
                            await session.MCPClient.RequestToolsList(nextCursor);
                        }
                        else
                        {
                            session.MCPClient.IsReady = true;
                            this.Logger.Information("All tools have been obtained, MCP client is ready.");

                            //// 刷新工具缓存，确保MCP工具被包含在函数列表中
                            //if (conn?.FuncHandler?.ToolManager != null)
                            //{
                            //    conn.FuncHandler.ToolManager.RefreshTools();
                            //    conn.FuncHandler.CurrentSupportFunctions();
                            //}
                        }
                    }
                    else
                    {
                        this.Logger.Error("工具列表格式错误");
                    }
                    return;
                }
            }
            else if (jsonObject.TryGetPropertyValue("method", out var method) && method != null)
            {

            }
            else if (jsonObject.TryGetPropertyValue("error", out var error) && error != null)
            {

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

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}
