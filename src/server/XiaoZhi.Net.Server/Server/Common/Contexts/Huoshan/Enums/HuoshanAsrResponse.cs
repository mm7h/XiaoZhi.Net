using System.Text.Json.Nodes;

namespace XiaoZhi.Net.Server.Common.Contexts.Huoshan.Enums
{
    internal sealed record HuoshanAsrResponse(
        MsgType MessageType,
        byte Flags,
        int Sequence,
        int EventType,
        int ErrorCode,
        JsonNode? Payload)
    {
        public bool IsLastPackage => (this.Flags & 0b0010) != 0;
    }
}
