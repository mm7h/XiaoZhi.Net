using System.IO;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Resources
{
    internal interface IDeviceBinding : IResource<DeviceBindSetting>
    {
        Stream? GetDeviceNotFoundAudioStream();
        Stream? GetDeviceBindCodeAudioStream(string bindCode);
    }
}
