using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
    internal class OutAudioSegmentPolicy : PooledObjectPolicy<OutAudioSegment>
    {
        public override OutAudioSegment Create()
        {
            return new OutAudioSegment();
        }

        public override bool Return(OutAudioSegment obj)
        {
            obj.Reset();
            return true;
        }
    }
}