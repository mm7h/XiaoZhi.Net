using System.Text.Json.Nodes;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models
{
    internal record TTSHttpResponseChunk(int? Code, string Message, string? Data, JsonObject? Sentence);
}
