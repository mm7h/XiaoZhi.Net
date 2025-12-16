using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioProcessor : IProvider<AudioSetting>
    {
        event Action<float[], bool, bool> OnMixedAudioDataAvailable;
        event Action<AudioType, string, Emotion> OnSubtitleStart;
        event Action<AudioType, string, Emotion> OnSubtitleEnd;

        void ProcessAudio(AudioType audioType, float[] audioData, string content, Emotion emotion, bool isFirstFrame, bool isLastFrame);
        void CompleteStream(AudioType audioType);
        void ClearAllBuffers();
        void SealCurrentSubtitle(AudioType audioType);
    }
}