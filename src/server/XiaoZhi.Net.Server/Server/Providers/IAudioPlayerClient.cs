using XiaoZhi.Net.Server.Providers.AudioPlayer;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioPlayerClient : IProvider<AudioSetting>
    {
        IMusicPlayer MusicPlayer { get; }
        ISystemNotification SystemNotification { get; }
        /// <summary>
        /// 音乐播放器或通知播放器是否有音频正在播放
        /// </summary>
        bool IsPlaying { get; }
    }
}
