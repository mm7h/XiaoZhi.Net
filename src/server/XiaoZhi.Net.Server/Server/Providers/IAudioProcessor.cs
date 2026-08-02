using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Media.Abstractions.Dtos;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioProcessor : IProvider<AudioSetting>
    {
        event Action<float[], bool, bool, string?> OnMixedAudioDataAvailable;

        void ProcessAudio(AudioType audioType, float[] audioData, string content, Emotion emotion, bool isFirstFrame, bool isLastFrame, string? sentenceId);
        void CompleteStream(AudioType audioType);
        void ClearAllBuffers();
        void RegisterSubtitle(string sentenceId, AudioType audioType, TtsStatus ttsStatus, string text, Emotion emotion);
        bool GetSubtitle(string sentenceId, out AudioSubtitle subtitle);
    }

}
