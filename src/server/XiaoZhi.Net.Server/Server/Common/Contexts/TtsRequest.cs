using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class TtsRequest
    {
        public TtsRequest(string sessionId, string deviceId, string content, bool isFirstSegment, bool isLastSegment, CancellationToken token)
        {
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.Content = content;
            this.IsFirstSegment = isFirstSegment;
            this.IsLastSegment = isLastSegment;
            this.ResultTcs = new TaskCompletionSource<TtsResponse>();
            this.Token = token;
        }

        public string SessionId { get; }
        public string DeviceId { get; }
        public string Content { get; }
        public bool IsFirstSegment { get; }
        public bool IsLastSegment { get; }
        public TaskCompletionSource<TtsResponse> ResultTcs { get; }
        public CancellationToken Token { get; }
    }
}
