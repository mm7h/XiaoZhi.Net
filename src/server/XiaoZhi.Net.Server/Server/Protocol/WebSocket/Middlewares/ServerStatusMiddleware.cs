using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Abstractions.Middleware;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using XiaoZhi.Net.Server.Management;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Middlewares
{
    internal class ServerStatusMiddleware : MiddlewareBase
    {
        private readonly ILogger _logger;
        public ServerStatusMiddleware(ILogger<ServerStatusMiddleware> logger)
        {
            this.Order = 1001;
            this._logger = logger;
        }
        public override void Start(IServer server)
        {
            ListenOptions? listenOption = server.Options.Listeners.FirstOrDefault();
            if (listenOption is not null)
            {
                string listeningUrl = $"{(listenOption.AuthenticationOptions is not null ? "wss://" : "ws://")}{this.GetLocalIP()}:{listenOption.Port}{listenOption.Path}";
                this._logger.LogInformation("Server started and listing on: {listeningUrl}", listeningUrl);
            }
            else
            {
                this._logger.LogWarning("No listening options found for the server. Unable to determine listening URL.");
            }
        }

        public override void Shutdown(IServer server)
        {
            ResourceManager resourceManager = server.ServiceProvider.GetRequiredService<ResourceManager>();
            resourceManager.Dispose(server.ServiceProvider);

            ProviderManager providerManager = server.ServiceProvider.GetRequiredService<ProviderManager>();
            providerManager.Dispose(server.ServiceProvider);

            this._logger.LogInformation("Server is shutting down.");
            Serilog.Log.CloseAndFlush();
        }

        private string GetLocalIP()
        {
            string hostName = Dns.GetHostName();
            return Dns.GetHostAddresses(hostName).FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
        }
    }
}
