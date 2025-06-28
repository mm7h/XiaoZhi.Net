using Microsoft.Extensions.DependencyInjection;
using System;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Management
{
    internal sealed class ProtocolManager
    {

        public IProtocolEngine ProtocolEngine { get; private set; } = null!;

        public bool Started => this.ProtocolEngine?.Started ?? false;

        public ProtocolManager()
        {

        }

        public static void RegisterServices(IServiceCollection services, XiaoZhiConfig config)
        {
            if (config.ServerProtocol == ServerProtocol.WebSocket)
            {
                services.AddSingleton(config.WebSocketOption);
                services.AddSingleton<IProtocolEngine, WebSocketEngine>();
            }
            else
            {
                //MQTT
                throw new NotSupportedException("No MQTT implement yet...");
            }
            services.AddSingleton<ProtocolManager>();
        }

        public void BuildComponent(IServiceProvider serviceProvider)
        {
            IProtocolEngine protocolEngine = serviceProvider.GetRequiredService<IProtocolEngine>();
            protocolEngine.Build();
        }
    }
}
