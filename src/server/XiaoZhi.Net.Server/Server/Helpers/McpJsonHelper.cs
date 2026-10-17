using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace XiaoZhi.Net.Server.Helpers;

internal static class McpJsonHelper
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    // MCP 的字段及工具参数不能经过小智外层协议的 snake_case 转换。
    public static JsonNode? ToNode(object value) => JsonSerializer.SerializeToNode(value, s_options);
}
