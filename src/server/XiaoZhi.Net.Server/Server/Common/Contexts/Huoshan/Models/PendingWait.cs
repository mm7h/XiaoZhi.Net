using System;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Common.Contexts.Huoshan.Models
{
    internal class PendingWait
    {
        public required Func<Message, bool> Match { get; init; }
        public required TaskCompletionSource<Message> Tcs { get; init; }
    }
}
