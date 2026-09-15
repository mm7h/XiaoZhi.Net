using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Providers.AudioPlayer;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioPlayerClient : IProvider<AudioSetting>
    {
        IMusicPlayer MusicPlayer { get; }
        /// <summary>
        /// 音乐播放器是否有音频正在播放
        /// </summary>
        bool IsPlaying { get; }
    }
}
