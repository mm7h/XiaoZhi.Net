using Microsoft.Extensions.Logging;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
    internal class AudioPlayerClient : BaseProvider<AudioPlayerClient, AudioSetting>, IAudioPlayerClient
    {
        private readonly IMusicPlayer _musicPlayer;
        private readonly ISystemNotification _systemNotification;


        public AudioPlayerClient(IMusicPlayer musicPlayer, ISystemNotification systemNotification, ILogger<AudioPlayerClient> logger) : base(logger)
        {
            this._musicPlayer = musicPlayer;
            this._systemNotification = systemNotification;
        }
        public override string ProviderType => "audio player";

        public override string ModelName => nameof(AudioPlayerClient);

        public IMusicPlayer MusicPlayer => this._musicPlayer;
        public ISystemNotification SystemNotification => this._systemNotification;

        public override bool Build(AudioSetting audioSetting)
        {
            return this._musicPlayer.Build(audioSetting) && this._systemNotification.Build(audioSetting);
        }

        public override void Dispose()
        {
            this._musicPlayer.Dispose();
            this._systemNotification.Dispose();
        }
    }
}
