using SherpaOnnx;
using System.Threading;
using XiaoZhi.Net.Server.Providers.ASR;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class AsrRequest
    {
        public AsrRequest(string sessionId, string deviceId, OfflineStream stream, int sampleRate, int frameSize, IAsrEventCallback callback,  CancellationToken token)
        {
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.Stream = stream;
            this.SampleRate = sampleRate;
            this.FrameSize = frameSize;
            this.Callback = callback;
            this.Token = token;
        }

        public string SessionId { get; set; }
        public string DeviceId { get; set; }
        public OfflineStream Stream { get; set; }
        public int SampleRate { get; set; }
        public int FrameSize { get; set; }
        public IAsrEventCallback Callback { get; set; }
        public CancellationToken Token { get; set; }
    }
}
