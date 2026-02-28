using System.ComponentModel;

namespace XiaoZhi.Net.Server.Abstractions.Common.Enums
{
    public enum TtsStatus
    {
        [Description("start")]
        Start,
        [Description("stop")]
        Stop,
        [Description("sentence_start")]
        SentenceStart,
        [Description("sentence_end")]
        SentenceEnd
    }
}
