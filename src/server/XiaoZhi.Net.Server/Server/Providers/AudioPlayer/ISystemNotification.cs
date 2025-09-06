using System;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
    internal interface ISystemNotification : IProvider<AudioSetting>
    {
        event Action<float[], bool, bool>? OnAudioData;
        Task PlayBindCodeAsync(string bindCode);
        Task PlayNotFoundAsync();
    }
}
