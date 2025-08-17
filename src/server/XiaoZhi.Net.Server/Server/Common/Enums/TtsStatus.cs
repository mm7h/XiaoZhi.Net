using System.ComponentModel;

namespace XiaoZhi.Net.Server.Common.Enums
{
    internal enum TtsStatus
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
