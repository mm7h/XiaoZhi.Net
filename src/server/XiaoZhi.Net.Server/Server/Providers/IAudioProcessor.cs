using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioProcessor : IProvider<AudioSetting>
    {
        event Action<float[], bool, bool> OnMixedAudioDataAvailable;
        event Action<AudioType, string> OnSubtitleStart;
        event Action<AudioType, string> OnSubtitleEnd;

        void ProcessAudio(AudioType audioType, float[] audioData, string text, bool isFirstFrame, bool isLastFrame);

        void CompleteStream(AudioType audioType);

        void ClearAllBuffers();
    }
}