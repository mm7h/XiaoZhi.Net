using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Host;
using SuperSocket.WebSocket.Server;
using System;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Protocol.WebSocket.Contexts;
using XiaoZhi.Net.Server.Protocol.WebSocket.Handlers;
using XiaoZhi.Net.Server.Common.Constants;

namespace XiaoZhi.Net.Server.Management
{
    internal class ProtocolManager
    {
        public static IHostBuilder RegisterServices(IHostBuilder builder, XiaoZhiConfig config)
        {
            if (config.ServerProtocol == ServerProtocol.WebSocket)
            {
                WebSocketServerOption webSocketOption = config.WebSocketServerOption;

                return builder.ConfigureServices((context, services) =>
                    {
                        services.AddSingleton<ProtocolManager>();
                        services.Configure<HandshakeOptions>(opt =>
                        {
                            opt.HandshakeValidator = AuthenticationVerification.VerifyAsync;
                        });
                    }).AsWebSocketHostBuilder()
                    .ConfigureSuperSocket(options =>
                    {
                        options.Name = GlobalVariables.ServerName;

                        ListenOptions listenOptions = new ListenOptions
                        {
                            Ip = webSocketOption.IP,
                            Port = webSocketOption.Port,
                            Path = webSocketOption.Path
                        };

                        if (webSocketOption.WssOption is not null)
                        {
                            listenOptions.AuthenticationOptions.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(webSocketOption.WssOption.CertFilePath, webSocketOption.WssOption.CertPassword);
                        }
                        options.Listeners = new List<ListenOptions> { listenOptions };
                        options.IdleSessionTimeOut = 60;
                        options.ClearIdleSessionInterval = 30;
                    })
                    .UseWebSocketMessageHandler(MessageDispatch.DispatchAsync)
                    .UseServerStatusMonitor()
                    .UseXiaoZhiSessionContainer()
                    .UseSession<SocketSession>()
                    .UseClearIdleSession();
            }
            else
            {
                //MQTT
                throw new NotSupportedException("No MQTT implement yet...");
            }

        }
    }
}
