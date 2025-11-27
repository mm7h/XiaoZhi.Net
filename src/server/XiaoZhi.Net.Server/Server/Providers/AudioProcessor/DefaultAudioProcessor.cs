using Microsoft.Extensions.Logging;
using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.AudioMixer
{
    internal class DefaultAudioProcessor : BaseProvider<DefaultAudioProcessor, AudioSetting>, IAudioProcessor
    {
        private readonly IAudioMixer _audioMixer;
        private readonly IAudioSubtitleSyncTracker _audioSubtitleSyncTracker;

        public event Action<float[], bool, bool>? OnMixedAudioDataAvailable;
        public event Action<AudioType, string>? OnSubtitleStart;
        public event Action<AudioType, string>? OnSubtitleEnd;

        public DefaultAudioProcessor(IAudioMixer audioMixer, IAudioSubtitleSyncTracker audioSubtitleSyncTracker, ILogger<DefaultAudioProcessor> logger) : base(logger)
        {
            this._audioMixer = audioMixer;
            this._audioSubtitleSyncTracker = audioSubtitleSyncTracker;
            this._audioMixer.OnMixedAudioDataAvailable += this.FireOnMixedAudioData;

            // 订阅字幕跟踪器事件并向外转发
            this._audioSubtitleSyncTracker.OnSubtitleStart += this.FireOnSubtitleStart;
            this._audioSubtitleSyncTracker.OnSubtitleEnd += this.FireOnSubtitleEnd;
        }
        public override string ProviderType => "AudioProcessor";

        public override string ModelName => "default audio processor";

        public override bool Build(AudioSetting settings)
        {
            try
            {
                if (this._audioMixer.IsInitialized)
                {
                    this.Logger.LogWarning("The audio mixer has been initialized, no need to initialize again.");
                    return true;
                }
                // 将字幕同步跟踪器注入混音器以实现音频-字幕对齐
                this._audioMixer.Initialize(settings.SampleRate, settings.Channels, settings.FrameDuration, null, this._audioSubtitleSyncTracker);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public void ProcessAudio(AudioType audioType, float[] audioData, string text, bool isFirstFrame, bool isLastFrame)
        {
            // Add audio to mixer regardless of frame type (even empty frames help timing)
            if (audioData.Length > 0)
            {
                this._audioMixer.AddAudioData(audioType, audioData);
            }

            int channels = Math.Max(1, this._audioMixer.OutputChannels);
            int monoSamples = audioData.Length / channels;

            if (isFirstFrame)
            {
                this._audioSubtitleSyncTracker.RegisterAudioSubtitle(audioType, text);
                if (monoSamples > 0)
                {
                    this._audioSubtitleSyncTracker.AttachSamplesToNextSubtitle(audioType, monoSamples);
                }
            }
            else if (monoSamples > 0)
            {
                this._audioSubtitleSyncTracker.AttachSamplesToNextSubtitle(audioType, monoSamples);
            }
        }

        public void CompleteStream(AudioType audioType)
        {
            this._audioMixer.StopAudioStream(audioType);
        }

        public void SealCurrentSubtitle(AudioType audioType)
        {
            this._audioSubtitleSyncTracker.SealCurrentSubtitle(audioType);
        }

        public void ClearAllBuffers()
        {
            this._audioSubtitleSyncTracker.ClearAll();
            this._audioMixer.ClearAllBuffers();
        }


        private void FireOnMixedAudioData(float[] audioPcmData, bool isFirst, bool isLast)
        {
            this.OnMixedAudioDataAvailable?.Invoke(audioPcmData, isFirst, isLast);
        }

        private void FireOnSubtitleStart(AudioType audioType, string text)
        {
            this.OnSubtitleStart?.Invoke(audioType, text);
        }

        private void FireOnSubtitleEnd(AudioType audioType, string text)
        {
            this.OnSubtitleEnd?.Invoke(audioType, text);
        }

        public override void Dispose()
        {
            this._audioSubtitleSyncTracker.OnSubtitleStart -= this.FireOnSubtitleStart;
            this._audioSubtitleSyncTracker.OnSubtitleEnd -= this.FireOnSubtitleEnd;
            this._audioSubtitleSyncTracker.ClearAll();
            this._audioMixer.ClearAllBuffers();
            this._audioMixer.OnMixedAudioDataAvailable -= this.FireOnMixedAudioData;
        }
    }
}
