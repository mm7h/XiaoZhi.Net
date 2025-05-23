using System.Collections.Generic;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server
{
    public sealed class XiaoZhiConfig
    {
        public ServerProtocol ServerProtocol { get; set; }
        public string Prompt { get; set; } = null!;
        public int? CloseConnectionNoVoiceTime { get; set; }
        public LogSetting LogSetting { get; set; } = null!;
        public WebSocketOption WebSocketOption { get; set; } = null!;
        public AuthOption AuthOption { get; set; } = null!;
        public AudioSetting AudioSetting { get; set; } = null!;
        public ModelSetting VadSetting { get; set; } = null!;
        public ModelSetting AsrSetting { get; set; } = null!;
        public ModelSetting PunctuationSetting { get; set; } = null!;
        public ModelSetting LlmSetting { get; set; } = null!;
        public ModelSetting MemorySetting { get; set; } = null!;
        public ModelSetting TtsSetting { get; set; } = null!;
        public ModelSetting? IntentSetting { get; set; }
    }
    #region Auth
    public sealed class AuthOption
    {
        public bool Enabled { get; set; }
        public IList<Tokens> Tokens { get; set; }
        public IList<string> AllowedDevices { get; set; }
    }
    public sealed class Tokens
    {
        public string Token { get; set; }
        public string Name { get; set; }
    }
    #endregion

    #region Log
    public sealed class LogSetting
    {
        public string LogLevel { get; set; }
        public string LogFilePath { get; set; }
        public string OutputTemplate { get; set; }
        public int RetainedFileCountLimit { get; set; }
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
