using System.Runtime.InteropServices;

namespace XiaoZhi.Net.Server.Media.Utilities;

internal static class PlatformInfo
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static bool IsOSX => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}
