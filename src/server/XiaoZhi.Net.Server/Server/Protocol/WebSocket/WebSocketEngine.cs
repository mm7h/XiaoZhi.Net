using Serilog;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using WebSocketSharp.Server;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    internal sealed class WebSocketEngine : IProtocolEngine
    {
        private readonly WebSocketOption _webSocketOption;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;
        private string? _path;
        private WebSocketServer? _server;

        public WebSocketEngine(WebSocketOption webSocketOption, IServiceProvider serviceProvider, ILogger logger)
        {
            this._webSocketOption = webSocketOption;
            this._serviceProvider = serviceProvider;
            this._logger = logger;
        }

        public bool Started => this._server?.IsListening ?? false;

        public void Build()
        {
            if (this._webSocketOption == null)
            {
                throw new ArgumentNullException(nameof(this._webSocketOption));
            }
            string url = this._webSocketOption.Url;
            bool isWss = url.ToLower().StartsWith("wss");
            this._path = this._webSocketOption.Path;
            this._server = new WebSocketServer(url)
            {
                //KeepClean = true
            };
            if (isWss)
            {
                WssOption? wssOption = this._webSocketOption.WssOption;
                if (wssOption == null)
                {
                    throw new ArgumentNullException(nameof(wssOption));
                }
                this._server.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(wssOption.CertFilePath, wssOption.CertPassword);
            }
#pragma warning disable CS0618
            this._server!.AddWebSocketService<WebSocketService>(this._path, () =>
            {
                return new WebSocketService(this._serviceProvider);
            });
#pragma warning restore CS0618
        }

        public Task StartAsync()
        {
            this._server!.Start();
            string listeningUrl = $"{(this._server.IsSecure ? "wss://" : "ws://")}{this.GetLocalIP()}:{this._server.Port}{this._path}";
            this._logger.Information("Server started and listing on: {listeningUrl}", listeningUrl);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            this._server!.Stop();
            return Task.CompletedTask;
        }

        private string GetLocalIP()
        {
            string hostName = Dns.GetHostName();
            return Dns.GetHostAddresses(hostName).FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
        }
    }
}
