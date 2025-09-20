using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
    internal sealed class MixedAudioPacketPolicy : PooledObjectPolicy<MixedAudioPacket>
    {
        public override MixedAudioPacket Create()
        {
            return new MixedAudioPacket();
        }

        public override bool Return(MixedAudioPacket obj)
        {
            // 清理对象状态，为下次使用做准备
            obj.Reset();
            return true;
        }
    }
}