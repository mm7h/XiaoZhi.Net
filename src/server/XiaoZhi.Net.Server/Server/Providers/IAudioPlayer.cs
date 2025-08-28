using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioPlayer : IProvider<AudioSetting>
    {
        event Action<string>? OnBeforeProcessing;
        event Action<string, float[]>? OnProcessing;
        event Action<string, bool>? OnProcessed;

        PlaybackState PlaybackState { get; }
        Task PlayAsync(params string[] sources);
        Task PauseAsync();
        Task ResumeAsync();
        Task SeekAsync(TimeSpan position);
        Task StopAsync();
    }
}
