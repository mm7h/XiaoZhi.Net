using System.ComponentModel;

namespace XiaoZhi.Net.Server.Common.Enums
{
    internal enum AudioType
    {
        [Description("TTS")]
        TTS,
        [Description("System Notification")]
        SystemNotification,
        [Description("Music")]
        Music,
        [Description("Other")]
        Other
    }
}
