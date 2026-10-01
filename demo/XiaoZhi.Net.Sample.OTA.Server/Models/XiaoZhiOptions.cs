namespace Demo.OTA.Server.Models
{
    public class XiaoZhiOptions
    {
        public string? ConfigDirectory { get; set; }

        public WebSocketInfo Websocket { get; set; } = new WebSocketInfo();
    }
}
