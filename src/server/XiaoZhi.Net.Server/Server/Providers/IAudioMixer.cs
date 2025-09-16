using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// 音频混音器
    /// </summary>
    internal interface IAudioMixer : IProvider<AudioSetting>
    {
        /// <summary>
        /// 混合音频数据事件
        /// </summary>
        event Action<float[], bool, bool>? OnMixedAudioDataAvailable;

        /// <summary>
        /// 添加音频数据
        /// </summary>
        /// <param name="audioType">音频类型</param>
        /// <param name="audioData">音频数据</param>
        void AddAudioData(AudioType audioType, float[] audioData);

        /// <summary>
        /// 停止音频流
        /// </summary>
        /// <param name="audioType">音频类型</param>
        void StopAudioStream(AudioType audioType);

        /// <summary>
        /// 清除所有缓冲区
        /// </summary>
        void ClearAllBuffers();
    }
}