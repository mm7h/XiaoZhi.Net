using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server
{
    public sealed class XiaoZhiConfig
    {
        public ServerProtocol ServerProtocol { get; set; }
        public string Prompt { get; set; } = null!;
        public int? CloseConnectionNoVoiceTime { get; set; }
        public bool AuthEnabled { get; set; }
        public LogSetting LogSetting { get; set; } = new LogSetting();
        public WebSocketOption WebSocketOption { get; set; } = null!;
        public AudioSetting AudioSetting { get; set; } = null!;
        public ModelSetting VadSetting { get; set; } = null!;
        public ModelSetting AsrSetting { get; set; } = null!;
        public ModelSetting PunctuationSetting { get; set; } = null!;
        public ModelSetting LlmSetting { get; set; } = null!;
        public ModelSetting MemorySetting { get; set; } = null!;
        public ModelSetting TtsSetting { get; set; } = null!;
        public ModelSetting? IntentSetting { get; set; }
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
    public sealed class WebSocketOption
    {
        public string Url { get; set; } = null!;
        public string Path { get; set; } = null!;
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


}
