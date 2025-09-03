using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IDeviceBindingPlayer : IProvider<DeviceBindSetting>
    {
        event Action<PlaybackState>? OnPlayStateChanged;
        event Action<float[]>? OnAudioData;
        Task PlayBindCodeAsync(string bindCode, AudioSetting sessionAudioSetting);
        Task PlayNotFoundAsync(AudioSetting sessionAudioSetting);
    }
}
