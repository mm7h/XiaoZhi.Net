using SherpaOnnx;
using System.Threading;

namespace XiaoZhi.Net.Server.Providers.ASR.Contexts
{
    internal record AsrRequest(
        string SessionId,
        string DeviceId,
        OfflineStream Stream,
        int SampleRate,
        int FrameSize,
        IAsrEventCallback Callback,
        CancellationToken Token
    );
}
