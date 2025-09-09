using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Dtos;

namespace XiaoZhi.Net.Server.FFmpeg.Abstractions
{
    /// <summary>
    /// 音频混音器接口，支持多路音频流的实时混音
    /// </summary>
    public interface IAudioMixer : IDisposable
    {
        /// <summary>
        /// 混音器状态改变事件
        /// </summary>
        event Action<AudioMixerState> StateChanged;

        /// <summary>
        /// 混音后的音频数据事件
        /// </summary>
        event Action<float[], bool, bool> OnMixedAudioDataAvailable;

        /// <summary>
        /// 音频统计信息事件
        /// </summary>
        event Action<AudioMixerStats> OnStatsUpdated;

        /// <summary>
        /// 获取混音器是否已初始化
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 获取输出采样率
        /// </summary>
        int OutputSampleRate { get; }

        /// <summary>
        /// 获取输出声道数
        /// </summary>
        int OutputChannels { get; }

        /// <summary>
        /// 获取帧时长（毫秒）
        /// </summary>
        int FrameDuration { get; }

        /// <summary>
        /// 初始化混音器
        /// </summary>
        /// <param name="outputSampleRate">输出采样率</param>
        /// <param name="outputChannels">输出声道数</param>
        /// <param name="frameDuration">帧时长（毫秒）</param>
        /// <param name="config">混音器配置</param>
        /// <returns>是否初始化成功</returns>
        bool Initialize(int outputSampleRate, int outputChannels, int frameDuration, AudioMixerConfig? config = null);

        /// <summary>
        /// 添加音频数据到指定优先级的音频流
        /// </summary>
        /// <param name="audioType">音频类型（优先级）</param>
        /// <param name="audioData">音频数据</param>
        /// <param name="isFirst">是否为第一帧</param>
        /// <param name="isLast">是否为最后一帧</param>
        void AddAudioData(AudioType audioType, float[] audioData, bool isFirst, bool isLast);

        /// <summary>
        /// 停止指定类型的音频流
        /// </summary>
        /// <param name="audioType">音频类型</param>
        void StopAudioStream(AudioType audioType);

        /// <summary>
        /// 清空所有音频缓冲区
        /// </summary>
        void ClearAllBuffers();

        /// <summary>
        /// 获取当前混音统计信息
        /// </summary>
        AudioMixerStats GetCurrentStats();
    }

    /// <summary>
    /// 音频混音器状态
    /// </summary>
    public enum AudioMixerState
    {
        Idle,
        Mixing,
        Stopped
    }

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