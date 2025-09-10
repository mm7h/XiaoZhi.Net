using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
    internal sealed class OutAudioSegmentPolicy : PooledObjectPolicy<OutAudioSegment>
    {
        public override OutAudioSegment Create()
        {
            return new OutAudioSegment();
        }

        public override bool Return(OutAudioSegment obj)
        {
            // 清理对象状态，为下次使用做准备
            obj.Reset();
            return true;
        }
    }
}