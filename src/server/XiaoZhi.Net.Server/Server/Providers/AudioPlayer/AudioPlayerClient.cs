using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
    internal class AudioPlayerClient : BaseProvider<AudioPlayerClient, AudioSetting>, IAudioPlayerClient
    {
        private readonly IMusicPlayer _musicPlayer;
        public AudioPlayerClient(IMusicPlayer musicPlayer, ILogger<AudioPlayerClient> logger) : base(logger)
        {
            this._musicPlayer = musicPlayer;
        }
        public override string ProviderType => "audio player";

        public override string ModelName => nameof(AudioPlayerClient);

        public IMusicPlayer MusicPlayer => this._musicPlayer;
        public bool IsPlaying => this._musicPlayer.IsPlaying;

        public override bool Build(AudioSetting audioSetting)
        {
            return this._musicPlayer.Build(audioSetting);
        }

        public override void Dispose()
        {
            // 音乐播放器由会话 DI 作用域释放。
        }
    }
}
