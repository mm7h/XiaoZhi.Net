using System.Threading;

namespace XiaoZhi.Net.Server.Providers.LLM.Utils
{
    /// <summary>
    /// Supplies a monotonically increasing order shared by all agents in one LLM session.
    /// </summary>
    internal sealed class ChatHistorySequence
    {
        private long _value;

        public long Next() => Interlocked.Increment(ref this._value);
    }
}
