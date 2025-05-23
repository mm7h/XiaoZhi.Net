
using XiaoZhi.Net.Server.Server;

namespace XiaoZhi.Net.Server
{
    public sealed class EngineFactory
    {

        public static IServerBuilder GetServerBuilder()
        {
            return ServerBuilder.CreateServerBuilder();
        }
    }
}
