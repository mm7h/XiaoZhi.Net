using System.Globalization;
using System.Text.Json.Nodes;

namespace XiaoZhi.Net.Server.Protocol.WebSocket;

internal static class WebSocketProtocolValidation
{
    public static bool IsSupported(JsonObject hello, string? header)
    {
        if (!string.IsNullOrWhiteSpace(header) && (!int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out int headerVersion) || headerVersion != 1))
        {
            return false;
        }
        return !hello.TryGetPropertyValue("version", out JsonNode? version) || (version is JsonValue value && value.TryGetValue(out int protocolVersion) && protocolVersion == 1);
    }
}
