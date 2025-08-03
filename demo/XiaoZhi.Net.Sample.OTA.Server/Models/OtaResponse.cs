//The file is referenced from https://github.com/zhulige/xiaozhi-sharp

namespace Demo.OTA.Server.Models
{
    /// <summary>
    /// OTA响应模型
    /// </summary>
    public class OtaResponse
    {
        public ActivationInfo? Activation { get; set; }

        public MqttInfo? Mqtt { get; set; }

        public WebSocketInfo? Websocket { get; set; }

        public ServerTimeInfo? ServerTime { get; set; }

        public FirmwareInfo? Firmware { get; set; }
    }

    /// <summary>
    /// 激活信息
    /// </summary>
    public class ActivationInfo
    {
        public string Code { get; set; } = "";

        public string Message { get; set; } = "";
    }

    /// <summary>
    /// MQTT配置信息
    /// </summary>
    public class MqttInfo
    {
        public string Endpoint { get; set; } = "";

        public string ClientId { get; set; } = "";

        public string Username { get; set; } = "";

        public string Password { get; set; } = "";

        public string PublishTopic { get; set; } = "";
    }

    /// <summary>
    /// WebSocket配置信息
    /// </summary>
    public class WebSocketInfo
    {
        public string Url { get; set; } = "";

        public string Token { get; set; } = "";
    }

    /// <summary>
    /// 服务器时间信息
    /// </summary>
    public class ServerTimeInfo
    {
        public long Timestamp { get; set; }

        public string Timezone { get; set; } = "";

        public int TimezoneOffset { get; set; }
    }

    /// <summary>
    /// 固件信息
    /// </summary>
    public class FirmwareInfo
    {
        public string Version { get; set; } = "";

        public string Url { get; set; } = "";
    }

    /// <summary>
    /// OTA错误响应
    /// </summary>
    public class OtaErrorResponse
    {
        public string Error { get; set; } = "";
    }
}
