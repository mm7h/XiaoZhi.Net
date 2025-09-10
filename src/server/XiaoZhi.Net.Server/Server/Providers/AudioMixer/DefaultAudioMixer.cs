using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using IFFmpegAudioMixer = XiaoZhi.Net.Server.FFmpeg.Abstractions.IAudioMixer;

namespace XiaoZhi.Net.Server.Providers.AudioMixer
{
    internal class DefaultAudioMixer : BaseProvider<DefaultAudioMixer, AudioSetting>, IAudioMixer
    {
        private readonly IFFmpegAudioMixer _audioMixer;

        public event Action<float[], bool, bool, Dictionary<AudioType, string?>>? OnMixedAudioDataAvailable;

        public DefaultAudioMixer(IFFmpegAudioMixer audioMixer, ILogger<DefaultAudioMixer> logger) : base(logger)
        {
            this._audioMixer = audioMixer;
            this._audioMixer.OnMixedAudioDataAvailable += this.FireOnMixedAudioData;
        }
        public override string ProviderType => "AudioMixer";

        public override string ModelName => "default audio mixer";

        public override bool Build(AudioSetting settings)
        {
            try
            {
                if (this._audioMixer.IsInitialized)
                {
                    this.Logger.LogWarning("The audio mixer has been initialized, no need to initialize again.");
                    return true;
                }
                this._audioMixer.Initialize(settings.SampleRate, settings.Channels, settings.FrameDuration);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        private void FireOnMixedAudioData(float[] audioPcmData, bool isFirst, bool isLast, Dictionary<AudioType, string?> contentMap)
        { 
            this.OnMixedAudioDataAvailable?.Invoke(audioPcmData, isFirst, isLast, contentMap);
        }

        public void AddAudioData(AudioType audioType, float[] audioData, bool isFirst, bool isLast, string? content = null)
        {
            this._audioMixer.AddAudioData(audioType, audioData, isFirst, isLast, content);
        }

        public void StopAudioStream(AudioType audioType)
        {
            this._audioMixer.StopAudioStream(audioType);
        }

        public void ClearAllBuffers()
        {
            this._audioMixer.ClearAllBuffers();
        }

        public override void Dispose()
        {
            this._audioMixer.ClearAllBuffers();
            this._audioMixer.OnMixedAudioDataAvailable -= this.FireOnMixedAudioData;
            this._audioMixer.Dispose();
        }
    }
}
