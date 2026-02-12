using Microsoft.Extensions.Logging;
using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Dtos;

namespace XiaoZhi.Net.Server.Providers.AudioMixer
{
    internal class DefaultAudioProcessor : BaseProvider<DefaultAudioProcessor, AudioSetting>, IAudioProcessor
    {
        private readonly IAudioMixer _audioMixer;
        private readonly IAudioSubtitleRegister _audioSubtitleSyncTracker;

        public event Action<float[], bool, bool, string?>? OnMixedAudioDataAvailable;

        public DefaultAudioProcessor(IAudioMixer audioMixer, IAudioSubtitleRegister audioSubtitleSyncTracker, ILogger<DefaultAudioProcessor> logger) : base(logger)
        {
            this._audioMixer = audioMixer;
            this._audioSubtitleSyncTracker = audioSubtitleSyncTracker;
            this._audioMixer.OnMixedAudioDataAvailable += this.FireOnMixedAudioData;
        }

        public override string ProviderType => "AudioProcessor";

        public override string ModelName => nameof(DefaultAudioProcessor);

        public override bool Build(AudioSetting settings)
        {
            try
            {
                if (this._audioMixer.IsInitialized)
                {
                    this.Logger.LogWarning(Lang.DefaultAudioProcessor_Build_Initialized);
                    return true;
                }
                this._audioMixer.Initialize(settings.SampleRate, settings.Channels, settings.FrameDuration, null);

                return true;

            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DefaultAudioProcessor_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }
        }

        public void ProcessAudio(AudioType audioType, float[] audioData, string content, Emotion emotion, bool isFirstFrame, bool isLastFrame, string? sentenceId)
        {
            this._audioMixer.AddAudioData(audioType, audioData, sentenceId);
        }

        public void CompleteStream(AudioType audioType)
        {
            this._audioMixer.StopAudioStream(audioType);
        }

        public void ClearAllBuffers()
        {
            this._audioSubtitleSyncTracker.ClearAll();
            this._audioMixer.ClearAllBuffers();
        }

        public void RegisterSubtitle(string sentenceId, AudioType audioType, TtsStatus ttsStatus, string text, Emotion emotion)
        {
            this._audioSubtitleSyncTracker.Register(sentenceId, audioType, ttsStatus, text, emotion);
        }

        public bool GetSubtitle(string sentenceId, out AudioSubtitle subtitle)
        {
            return this._audioSubtitleSyncTracker.GetSubtitle(sentenceId, out subtitle);
        }

        private void FireOnMixedAudioData(float[] audioPcmData, bool isFirst, bool isLast, string? sentenceId)
        {
            try
            {
                this.OnMixedAudioDataAvailable?.Invoke(audioPcmData, isFirst, isLast, sentenceId);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DefaultAudioProcessor_FireOnMixedAudioData_InvokeError);
            }
        }


        public override void Dispose()
        {
            this._audioSubtitleSyncTracker.ClearAll();
            this._audioMixer.ClearAllBuffers();
            this._audioMixer.OnMixedAudioDataAvailable -= this.FireOnMixedAudioData;
        }
    }
}
