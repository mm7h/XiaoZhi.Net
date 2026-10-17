using System.Threading;
using SherpaOnnx;

namespace XiaoZhi.Net.Server.Providers.ASR.Contexts
{
    internal record AsrRequest(
        string SessionId,
        string DeviceId,
        OfflineStream Stream,
        int SampleRate,
        int FrameSize,
        long TurnId,
        IAsrEventCallback Callback,
        CancellationToken Token
    );
}
