using System;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Models
{
    internal sealed class PendingWait
    {
        public required Func<Message, bool> Match { get; init; }
        public required TaskCompletionSource<Message> Tcs { get; init; }
    }
}
