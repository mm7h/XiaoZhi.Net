using XiaoZhi.Net.Server.Abstractions.Common.Enums;

using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server
{
    public sealed class XiaoZhiConfig
    {
        public ServerProtocol ServerProtocol { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public bool AuthEnabled { get; set; }
        public LogSetting LogSetting { get; set; } = new LogSetting();
        public WebSocketServerOption WebSocketServerOption { get; set; } = new WebSocketServerOption();
        public MusicProviderSetting MusicProviderSetting { get; set; } = new MusicProviderSetting();
        public DeviceBindSetting DeviceBindSetting { get; set; } = new DeviceBindSetting();
        public Dictionary<string, string> SelectedSettings { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> ConfiguredSettings { get; set; } = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        public AudioSetting AudioSetting { get; set; } = null!;
        public Dictionary<string, ModelSetting>? McpSettings { get; set; }

    }

}
