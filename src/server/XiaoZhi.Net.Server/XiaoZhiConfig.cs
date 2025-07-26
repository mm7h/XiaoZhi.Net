using System.Collections.Generic;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server
{
    public sealed class XiaoZhiApiConfig
    {
        public string ManageApiUrl { get; set; } = null!;
        public string Secret { get; set; } = null!;
    }

    public sealed class XiaoZhiConfig
    {
        public ServerProtocol ServerProtocol { get; set; }
        public string Prompt { get; set; } = null!;
        public int? CloseConnectionNoVoiceTime { get; set; }
        public bool AuthEnabled { get; set; }
        public LogSetting LogSetting { get; set; } = new LogSetting();
        public WebSocketServerOption WebSocketServerOption { get; set; } = new WebSocketServerOption();
        public DeviceBindSetting DeviceBindSetting { get; set; } = new DeviceBindSetting();
        public AudioSetting AudioSetting { get; set; } = null!;
        public ModelSetting VadSetting { get; set; } = null!;
        public ModelSetting AsrSetting { get; set; } = null!;
        public ModelSetting PunctuationSetting { get; set; } = null!;
        public List<ModelSetting> LlmSettings { get; set; } = null!;
        public ModelSetting MemorySetting { get; set; } = null!;
        public ModelSetting TtsSetting { get; set; } = null!;
        public ModelSetting? IntentSetting { get; set; }
        public Dictionary<string, ModelSetting>? McpSettings { get; set; }

    }

    #region Log
    public sealed class LogSetting
    {
        public string LogLevel { get; set; } = "INFO";
        public string LogFilePath { get; set; } = "logs/server_log.log";
        public string OutputTemplate { get; set; } = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}";
        public int RetainedFileCountLimit { get; set; } = 7;
    }
    #endregion

    #region WebSocketSetting
    public sealed class WebSocketServerOption
    {
        public string Url { get; set; } = "ws://0.0.0.0:4530";
        public string Path { get; set; } = "/xiaozhi/v1/";
        public WssOption? WssOption { get; set; }
    }
    public sealed class WssOption
    {
        public string? CertFilePath { get; set; }
        public string? CertPassword { get; set; }
    }
    #endregion

    public sealed class ModelSetting
    {
        public string ModelName { get; set; } = null!;
        public dynamic Config { get; set; } = null!;
    }
    public sealed class AudioSetting
    {
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int FrameDuration { get; set; }
    }
    public sealed class DeviceBindSetting
    {
        public string BindCodePromptFilePath { get; set; } = "config/assets/bind_code.wav";
        public string BindCodeDigitFolderPath { get; set; } = "config/assets/bind_code";
        public string BindNotFoundFilePath { get; set; } = "config/assets/bind_not_found.wav";
    }

}
