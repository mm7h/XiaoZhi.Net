using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
    internal class MixedAudioPacketPolicy : PooledObjectPolicy<MixedAudioPacket>
    {
        public override MixedAudioPacket Create()
        {
            return new MixedAudioPacket();
        }

        public override bool Return(MixedAudioPacket obj)
        {
            obj.Reset();
            return true;
        }
    }
}