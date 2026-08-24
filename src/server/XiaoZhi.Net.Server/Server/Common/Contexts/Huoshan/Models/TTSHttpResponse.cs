using System.Text.Json.Nodes;

namespace XiaoZhi.Net.Server.Common.Contexts.Huoshan.Models
{
    internal record TTSHttpResponse(string Reqid, int Code, string Operation, string Message, int Sequence, string? Data, JsonObject? Addition);
}
