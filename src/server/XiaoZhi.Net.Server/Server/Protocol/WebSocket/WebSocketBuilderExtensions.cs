using Microsoft.Extensions.DependencyInjection;
using SuperSocket.Server;
using SuperSocket.Server.Abstractions.Host;
using SuperSocket.Server.Abstractions.Session;
using XiaoZhi.Net.Server.Protocol.WebSocket.Middlewares;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    internal static class WebSocketBuilderExtensions
    {
        public static ISuperSocketHostBuilder<TReceivePackage> UseServerStatusMonitor<TReceivePackage>(this ISuperSocketHostBuilder<TReceivePackage> builder)
        {
            return builder.UseMiddleware<ServerStatusMiddleware>();
        }

        public static ISuperSocketHostBuilder<TReceivePackage> UseXiaoZhiSessionContainer<TReceivePackage>(this ISuperSocketHostBuilder<TReceivePackage> builder)
        {
            return (builder
                .UseMiddleware<SessionContainerMiddleware>(s => s.GetRequiredService<SessionContainerMiddleware>())
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<SessionContainerMiddleware>();
                    services.AddSingleton<ISessionContainer>((s) => s.GetRequiredService<SessionContainerMiddleware>());
                    services.AddSingleton<IAsyncSessionContainer>((s) => s.GetRequiredService<ISessionContainer>().ToAsyncSessionContainer());
                }) as ISuperSocketHostBuilder<TReceivePackage>)!;
        }
    }
}
