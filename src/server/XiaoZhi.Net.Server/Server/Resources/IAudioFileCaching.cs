using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Resources
{
    internal interface IAudioFileCaching : IResource<DeviceBindSetting>
    {
        bool TryGetAudioData(string cacheKey, out float[]? audioData);
    }
}
