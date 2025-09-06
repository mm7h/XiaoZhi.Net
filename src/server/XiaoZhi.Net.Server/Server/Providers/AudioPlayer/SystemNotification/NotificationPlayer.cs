using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Resources;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer.SystemNotification
{
    internal class NotificationPlayer : BaseProvider<NotificationPlayer, AudioSetting>, ISystemNotification
    {
        private readonly IStreamAudioPlayer _streamAudioPlayer;
        private readonly IDeviceBinding _deviceBindingResources;

        private AudioSetting? _audioSetting;

        public event Action<float[], bool, bool>? OnAudioData;

        public override string ProviderType => "system notification stream audio player";

        public override string ModelName => nameof(NotificationPlayer);

        public PlaybackState PlaybackState => this._streamAudioPlayer.State;


        public NotificationPlayer(IStreamAudioPlayer streamAudioPlayer, IDeviceBinding deviceBindingResources, ILogger<NotificationPlayer> logger) : base(logger)
        {
            this._streamAudioPlayer = streamAudioPlayer; this._streamAudioPlayer.OnAudioDataAvailable += this.FireAudioData;
            this._deviceBindingResources = deviceBindingResources;
        }

        public override bool Build(AudioSetting audioSetting)
        {
            if (!this._streamAudioPlayer.CheckFFmpegInstalled())
            {
                this.Logger.LogError("Failed to initialize FFmpeg, please check your the ffmpeg path configuration.");
                return false;
            }
            this._audioSetting = audioSetting;
            return true;
        }



        public async Task PlayBindCodeAsync(string bindCode)
        {
            if (this._audioSetting is null)
            {
                this.Logger.LogError("The audio player is not built yet.");
                return;
            }
            Stream? bindCodeAudioStream = this._deviceBindingResources.GetDeviceBindCodeAudioStream(bindCode);
            if (bindCodeAudioStream is null)
            {
                this.Logger.LogError("Failed to get the bind code audio stream.");
                return;
            }

            using (bindCodeAudioStream)
            {
                await this._streamAudioPlayer.LoadAsync(bindCodeAudioStream, this._audioSetting.SampleRate, this._audioSetting.Channels, this._audioSetting.FrameDuration);
                this._streamAudioPlayer.Play(true);
            }
        }

        public async Task PlayNotFoundAsync()
        {
            if (this._audioSetting is null)
            {
                this.Logger.LogError("The audio player is not built yet.");
                return;
            }
            Stream? notFoundAudioStream = this._deviceBindingResources.GetDeviceNotFoundAudioStream();
            if (notFoundAudioStream is null)
            {
                this.Logger.LogError("Failed to get the not found audio stream.");
                return;
            }
            using (notFoundAudioStream)
            {
                await this._streamAudioPlayer.LoadAsync(notFoundAudioStream, this._audioSetting.SampleRate, this._audioSetting.Channels, this._audioSetting.FrameDuration);
                this._streamAudioPlayer.Play(true);
            }
        }

        private void FireAudioData(float[] pcmData, bool isFirst, bool isLast)
        {
            this.OnAudioData?.Invoke(pcmData, isFirst, isLast);
        }

        public override void Dispose()
        {
            this._streamAudioPlayer.OnAudioDataAvailable -= this.FireAudioData;
            this._streamAudioPlayer.Dispose();
        }
    }
}
