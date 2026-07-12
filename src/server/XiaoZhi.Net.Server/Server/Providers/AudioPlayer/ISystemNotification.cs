using System;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
    internal interface ISystemNotification : IProvider<AudioSetting>
    {
        event Action<float[], bool, bool>? OnAudioData;
        /// <summary>
        /// 通知音频是否正在播放
        /// </summary>
        bool IsPlaying { get; }
        Task PlayBindCodeAsync(string bindCode);
        Task PlayNotFoundAsync();
        Task StopAsync();
    }
}
