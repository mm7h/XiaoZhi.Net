using System.ComponentModel;

namespace XiaoZhi.Net.Server.Abstractions.Common.Enums
{
    public enum ServerProtocol
    {
        [Description("WebSocket")]
        WebSocket,
        [Description("Mqtt")]
        Mqtt,
    }
}
