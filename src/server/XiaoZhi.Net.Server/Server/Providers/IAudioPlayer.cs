using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioPlayer : IProvider<AudioPlayerConfig>
    {
        event Action<string>? OnBeforeProcessing;
        event Action<string, float[]>? OnProcessing;
        event Action<string, bool>? OnProcessed;

        PlayingStatus PlayingStatus { get; }
        Task PlayAsync(params string[] sources);
        Task PauseAsync();
        Task ResumeAsync();
        Task SeekAsync(long positionMs);
        Task StopAsync();
    }
}
