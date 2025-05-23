using System.ComponentModel;

namespace XiaoZhi.Net.Server.Common.Enums
{
    public enum ServerProtocol
    {
        [Description("WebSocket")]
        WebSocket,
        [Description("Mqtt")]
        Mqtt,
    }
}
