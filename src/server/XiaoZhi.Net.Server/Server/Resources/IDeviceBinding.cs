using System.IO;

namespace XiaoZhi.Net.Server.Resources
{
    internal interface IDeviceBinding : IResource<DeviceBindSetting>
    {
        Stream? GetDeviceNotFoundAudioStream();
        Stream? GetDeviceBindCodeAudioStream(string bindCode);
    }
}
