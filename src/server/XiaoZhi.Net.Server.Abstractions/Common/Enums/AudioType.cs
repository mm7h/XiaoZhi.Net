using System.ComponentModel;

namespace XiaoZhi.Net.Server.Abstractions.Common.Enums
{
    public enum AudioType
    {
        [Description("System Notification")]
        SystemNotification = 10,
        [Description("TTS")]
        TTS = 5,
        [Description("Music")]
        Music = 1,
        [Description("Other")]
        Other = 0
    }
}
