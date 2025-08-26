using Microsoft.Extensions.Hosting;
using XiaoZhi.Net.Server.Abstractions;

namespace XiaoZhi.Net.Server
{
    public static class EngineFactory
    {

        public static IServerBuilder CreateServerBuilder()
        {
            return ServerBuilder.CreateServerBuilder();
        }

        public static IServerBuilder AsXiaoZhiHostBuilder(this IHostBuilder hostBuilder)
        {
            return ServerBuilder.CreateServerBuilder(hostBuilder);
        }
    }
}
