using SherpaOnnx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal sealed class AsrRequest
    {
        public OfflineStream Stream { get; set; }
        public int SampleRate { get; set; }
        public int FrameSize { get; set; }
        public TaskCompletionSource<string> ResultTcs { get; set; }
        public CancellationToken Token { get; set; }
    }
}
