using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions
{
    public interface IAudioSubtitleSyncTracker : IDisposable
    {
        event Action<AudioType, string, Emotion>? OnSubtitleStart;
        event Action<AudioType, string, Emotion>? OnSubtitleEnd;

        void RegisterAudioSubtitle(AudioType audioType, string subtitleText, Emotion emotion);
        void RegisterAudioSubtitle(AudioType audioType, string subtitleText, int sampleCount, Emotion emotion);
        void AttachSamplesToNextSubtitle(AudioType audioType, int sampleCount);
        void NotifyAudioSamplesSent(AudioType audioType, int samplesSent);
        void NotifyAudioSendComplete(AudioType audioType);
        void ClearAudioType(AudioType audioType);
        void ClearAll();
        void SealCurrentSubtitle(AudioType audioType);
    }
}
