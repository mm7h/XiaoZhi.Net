using SherpaOnnx;
using System.Threading;
using XiaoZhi.Net.Server.Providers.ASR;

namespace XiaoZhi.Net.Server.Common.Contexts
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
