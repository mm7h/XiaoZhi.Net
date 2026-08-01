using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos
{
    /// <summary>
    /// 音频混音器统计信息
    /// </summary>
    public sealed class AudioMixerStats
    {
        /// <summary>
        /// 当前输出 RMS 电平
        /// </summary>
        public float CurrentRms { get; set; }

        /// <summary>
        /// 当前输出峰值
        /// </summary>
        public float CurrentPeak { get; set; }

        /// <summary>
        /// 当前动态增益（dB）
        /// </summary>
        public float CurrentGainDb { get; set; }

        /// <summary>
        /// 限制器触发次数
        /// </summary>
        public long LimiterTriggerCount { get; set; }

        /// <summary>
        /// 活跃的音频流数量
        /// </summary>
        public int ActiveStreamCount { get; set; }

        /// <summary>
        /// 各音频流的延迟补偿（毫秒）
        /// </summary>
        public Dictionary<AudioType, float> DelayCompensation { get; set; } = new();
    }
}
