using Microsoft.Extensions.Hosting;

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
