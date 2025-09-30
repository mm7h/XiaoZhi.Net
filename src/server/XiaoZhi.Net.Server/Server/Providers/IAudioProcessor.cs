using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioProcessor : IProvider<AudioSetting>
    {
        event Action<float[], bool, bool> OnMixedAudioDataAvailable;
        event Action<AudioType, string> OnSubtitleStart;
        event Action<AudioType, string> OnSubtitleEnd;

        void RegisterSubtitle(AudioType audioType, string text, bool isFirst, bool isLast);
        void RegisterSubtitle(AudioType audioType, string text, int sampleCount, bool isFirst, bool isLast);

        void AddAudioData(AudioType audioType, float[] audioData);

        void StopAudioStream(AudioType audioType);

        void ClearAllBuffers();
    }
}