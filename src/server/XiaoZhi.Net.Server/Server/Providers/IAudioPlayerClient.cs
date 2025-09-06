using XiaoZhi.Net.Server.Providers.AudioPlayer;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioPlayerClient : IProvider<AudioSetting>
    {
        IMusicPlayer MusicPlayer { get; }
        ISystemNotification SystemNotification { get; }
    }
}
