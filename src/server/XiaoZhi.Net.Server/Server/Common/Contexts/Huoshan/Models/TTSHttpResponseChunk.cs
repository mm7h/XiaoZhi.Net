using System.Text.Json.Nodes;

namespace XiaoZhi.Net.Server.Common.Contexts.Huoshan.Models
{
    internal record TTSHttpResponseChunk(int? Code, string Message, string? Data, JsonObject? Sentence);
}
