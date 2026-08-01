using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos
{
    /// <summary>
    /// 音频混音器配置
    /// </summary>
    public class AudioMixerConfig
    {
        /// <summary>
        /// 音量过渡时间（毫秒）
        /// </summary>
        public int VolumeTransitionDurationMs { get; set; } = 500;

        /// <summary>
        /// 音量过渡曲线类型
        /// </summary>
        public VolumeTransitionCurve TransitionCurve { get; set; } = VolumeTransitionCurve.Logarithmic;

        /// <summary>
        /// 是否启用平滑音量控制
        /// </summary>
        public bool EnableSmoothVolumeControl { get; set; } = true;

        /// <summary>
        /// 新流启动时的缓冲容忍度（允许的最小帧百分比）
        /// </summary>
        public float NewStreamBufferTolerance { get; set; } = 0.25f;

        /// <summary>
        /// 播放开始前要在缓冲区中预填充的帧数
        /// </summary>
        public int BufferPrefillFrames { get; set; } = 2;

        /// <summary>
        /// 丢弃最旧帧之前的最大输出缓冲区大小，以防止过度延迟
        /// </summary>
        public int MaxOutputBufferFrames { get; set; } = 12;

        /// <summary>
        /// 系统通知类型的音量配置
        /// </summary>
        public AudioVolumeConfig SystemNotificationVolumeConfig { get; set; } = new AudioVolumeConfig(AudioType.SystemNotification, 1.0f, 1.0f);

        /// <summary>
        /// TTS类型的音量配置
        /// </summary>
        public AudioVolumeConfig TTSVolumeConfig { get; set; } = new AudioVolumeConfig(AudioType.TTS, 0.9f, 0.4f);

        /// <summary>
        /// 音乐类型的音量配置
        /// </summary>
        public AudioVolumeConfig MusicVolumeConfig { get; set; } = new AudioVolumeConfig(AudioType.Music, 0.6f, 0.1f);
    }
}
