using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace XiaoZhi.Net.Server.Helpers
{
    internal static class JsonHelper
    {
        public static readonly JsonSerializerOptions OPTIONS = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static string ToJson(this object obj) => JsonSerializer.Serialize(obj, JsonHelper.OPTIONS);
        public static JsonNode? ToNode(this object obj) => obj is null ? null : JsonSerializer.SerializeToNode(obj, JsonHelper.OPTIONS);
        public static string Serialize(object obj)
        {
            if (obj is string str)
            {
                return str;
            }
            else
            {
                return JsonSerializer.Serialize(obj, JsonHelper.OPTIONS);
            }
        }
        public static string Serialize(JsonObject obj) => obj.ToJsonString(JsonHelper.OPTIONS);
        public static byte[] SerializeToUtf8Bytes(object obj) => JsonSerializer.SerializeToUtf8Bytes(obj, JsonHelper.OPTIONS);
        public static JsonElement ToJsonElement(string json) => JsonSerializer.Deserialize<JsonElement>(json, JsonHelper.OPTIONS);

        public static T? Deserialize<T>(string json) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonHelper.OPTIONS);
            }
            catch
            {
                return null;
            }
        }
    }
}
