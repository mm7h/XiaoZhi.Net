using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
    internal interface IMusicPlayer : IProvider<AudioSetting>
    {
        event Action<float[], bool, bool> OnAudioData;
        string? PlayingMusicName { get; }
        PlaybackState PlaybackState { get; }
        bool IsPlaying { get; }
        float Volume { get; set; }
        Task PlayAsync(CancellationToken cancellationToken = default, params string[] sources);
        Task PauseAsync();
        Task ResumeAsync();
        Task SeekAsync(TimeSpan position);
        Task StopAsync();
    }
}
