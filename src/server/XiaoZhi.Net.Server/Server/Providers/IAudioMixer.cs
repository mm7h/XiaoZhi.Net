using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// 音频混音器提供程序接口
    /// </summary>
    internal interface IAudioMixer : IProvider<AudioSetting>
    {
        /// <summary>
        /// 混音后的音频数据事件
        /// </summary>
        event Action<float[], bool, bool> OnMixedAudioDataAvailable;

        /// <summary>
        /// 获取是否已初始化
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 添加音频数据
        /// </summary>
        /// <param name="audioType">音频类型</param>
        /// <param name="audioData">音频数据</param>
        /// <param name="isFirst">是否为第一帧</param>
        /// <param name="isLast">是否为最后一帧</param>
        void AddAudioData(AudioType audioType, float[] audioData, bool isFirst, bool isLast);

        /// <summary>
        /// 停止音频流
        /// </summary>
        /// <param name="audioType">音频类型</param>
        void StopAudioStream(AudioType audioType);

        /// <summary>
        /// 清空所有缓冲区
        /// </summary>
        void ClearAllBuffers();
    }
}