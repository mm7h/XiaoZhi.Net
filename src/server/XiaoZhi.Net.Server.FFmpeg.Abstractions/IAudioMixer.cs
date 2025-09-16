using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.FFmpeg.Abstractions
{
    /// <summary>
    /// 音频混音器接口，支持多路音频输入实时混音
    /// </summary>
    public interface IAudioMixer : IDisposable
    {
        /// <summary>
        /// 混音器状态改变事件
        /// </summary>
        event Action<AudioMixerState> StateChanged;

        /// <summary>
        /// 混合音频数据事件
        /// </summary>
        event Action<float[], bool, bool>? OnMixedAudioDataAvailable;

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
        void AddAudioData(AudioType audioType, float[] audioData);

        /// <summary>
        /// 停止指定类型的音频流
        /// </summary>
        /// <param name="audioType">音频类型</param>
        void StopAudioStream(AudioType audioType);

        /// <summary>
        /// 清除所有音频缓冲区
        /// </summary>
        void ClearAllBuffers();

        /// <summary>
        /// 获取当前混音统计信息
        /// </summary>
        AudioMixerStats GetCurrentStats();
    }
}