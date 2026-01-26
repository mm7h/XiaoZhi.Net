using System.Text.Json.Nodes;

namespace XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Models
{
    internal record TTSHttpResponseChunk(int? Code, string? Message, string? Data, JsonObject? Sentence);
}
