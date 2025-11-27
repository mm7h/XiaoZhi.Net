using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions
{
    public interface IAudioSubtitleSyncTracker : IDisposable
    {
        event Action<AudioType, string>? OnSubtitleStart;
        event Action<AudioType, string>? OnSubtitleEnd;

        void RegisterAudioSubtitle(AudioType audioType, string subtitleText);
        void RegisterAudioSubtitle(AudioType audioType, string subtitleText, int sampleCount);
        void AttachSamplesToNextSubtitle(AudioType audioType, int sampleCount);
        void NotifyAudioSamplesSent(AudioType audioType, int samplesSent);
        void NotifyAudioSendComplete(AudioType audioType);
        void ClearAudioType(AudioType audioType);
        void ClearAll();
        void SealCurrentSubtitle(AudioType audioType);
    }
}
