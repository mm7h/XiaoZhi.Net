using Microsoft.Extensions.Hosting;
using XiaoZhi.Net.Server.Management;

namespace XiaoZhi.Net.Server
{
    internal static class ServerBuilderExtensions
    {
        public static IHostBuilder RegisterLogger(this IHostBuilder builder, XiaoZhiConfig config)
        {
            return LoggerManager.RegisterServices(builder, config);
        }

        public static IHostBuilder RegisterResources(this IHostBuilder builder)
        {
            return ResourceManager.RegisterServices(builder);
        }

        public static IHostBuilder RegisterProviders(this IHostBuilder builder, XiaoZhiConfig config)
        {
            return ProviderManager.RegisterServices(builder, config);
        }

        public static IHostBuilder RegisterHandlers(this IHostBuilder builder)
        {
            return HandlerManager.RegisterServices(builder);
        }

        public static IHostBuilder RegisterProtocol(this IHostBuilder builder, XiaoZhiConfig config)
        {
            return ProtocolManager.RegisterServices(builder, config);
        }
    }
}
