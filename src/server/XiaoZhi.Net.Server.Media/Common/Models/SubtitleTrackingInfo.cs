using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Common.Models
{
    internal class SubtitleTrackingInfo
    {
        public AudioType AudioType { get; set; }
        public string SubtitleText { get; set; } = string.Empty;
        public DateTime RegisterTime { get; set; }
        public bool SubtitleStartSent { get; set; }
        public bool SubtitleEndSent { get; set; }
        public bool IsAudioStarted { get; set; }
        public bool IsAudioCompleted { get; set; }

        // Sample-based tracking (mono sample counts)
        public int TotalSamples { get; set; }
        public int RemainingSamples { get; set; }
    }
}
