using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server
{
    public sealed class XiaoZhiConfig
    {
        public ServerProtocol ServerProtocol { get; set; }
        public string Prompt { get; set; } = null!;
        public int? CloseConnectionNoVoiceTime { get; set; }
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
        public string IP { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 4530;
        public string Path { get; set; } = "/xiaozhi/v1/";
        public WssOption? WssOption { get; set; }
    }
    public sealed class WssOption
    {
        public string CertFilePath { get; set; } = "";
        public string? CertPassword { get; set; }
    }
    #endregion

    public sealed class ModelSetting
    {
        public string ModelName { get; set; } = null!;
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();
    }
    public sealed class AudioSetting
    {
        public AudioSetting()
        {

        }

        public AudioSetting(string format, int sampleRate, int channels, int frameDuration)
        {
            this.Format = format;
            this.SampleRate = sampleRate;
            this.Channels = channels;
            this.FrameDuration = frameDuration;
        }

        public string Format { get; set; } = "opus";
        public int SampleRate { get; set; } = 16000;
        public int Channels { get; set; } = 1;
        public int FrameDuration { get; set; } = 60;
        public int FrameSize => this.SampleRate * this.FrameDuration * this.Channels / 1000;
        public int OutSampleRate { get; set; } = 24000;
    }
    public sealed class MusicProviderSetting
    {
        public string MusicFolderPath { get; set; } = "musics";
    }
    public sealed class DeviceBindSetting
    {
        public string BindCodePromptFilePath { get; set; } = "configs/assets/bind_code.wav";
        public string BindCodeDigitFolderPath { get; set; } = "configs/assets/bind_code";
        public string BindNotFoundFilePath { get; set; } = "configs/assets/bind_not_found.wav";
    }

}
