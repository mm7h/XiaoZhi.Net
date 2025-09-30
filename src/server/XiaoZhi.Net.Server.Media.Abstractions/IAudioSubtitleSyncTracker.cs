using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions
{
    public interface IAudioSubtitleSyncTracker : IDisposable
    {
        event Action<AudioType, string>? OnSubtitleStart;
        event Action<AudioType, string>? OnSubtitleEnd;  
        
        /// <summary>
        /// 注册音频-字幕配对信息（不包含样本数，后续可通过 AttachSamplesToNextSubtitle 附加）
        /// </summary>
        void RegisterAudioSubtitle(AudioType audioType, string subtitleText,
            bool isFirstSegment, bool isLastSegment);

        /// <summary>
        /// 注册音频-字幕配对信息，并提供该字幕对应的音频样本数（单声道样本数）。
        /// </summary>
        void RegisterAudioSubtitle(AudioType audioType, string subtitleText, int sampleCount,
            bool isFirstSegment, bool isLastSegment);

        /// <summary>
        /// 为队列中下一个未绑定样本数的字幕附加音频样本数。
        /// </summary>
        void AttachSamplesToNextSubtitle(AudioType audioType, int sampleCount);

        /// <summary>
        /// 通知在本帧中该类型音频实际发送了多少个样本，用于推进字幕进度并触发开始/结束事件。
        /// </summary>
        void NotifyAudioSamplesSent(AudioType audioType, int samplesSent);

        /// <summary>
        /// 通知音频发送完成（对应混音器的最后一帧输出）
        /// </summary>
        void NotifyAudioSendComplete(AudioType audioType);

        /// <summary>
        /// 清理指定音频类型的所有跟踪信息
        /// </summary>
        void ClearAudioType(AudioType audioType);

        /// <summary>
        /// 清理所有跟踪信息
        /// </summary>
        void ClearAll();
    }
}
