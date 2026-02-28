using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
    internal class OutSegmentPolicy : PooledObjectPolicy<OutSegment>
    {
        public override OutSegment Create()
        {
            return new OutSegment();
        }

        public override bool Return(OutSegment obj)
        {
            obj.Reset();
            return true;
        }
    }
}