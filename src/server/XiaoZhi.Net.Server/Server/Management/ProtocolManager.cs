using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        public static void RegisterServices(HostApplicationBuilder builder, XiaoZhiConfig config)
        {
            if (config.ServerProtocol == ServerProtocol.WebSocket)
            {
                builder.Services.AddSingleton(config.WebSocketServerOption);
                builder.Services.AddSingleton<IProtocolEngine, WebSocketServerEngine>();
            }
            else
            {
                //MQTT
                throw new NotSupportedException("No MQTT implement yet...");
            }
            builder.Services.AddSingleton<ProtocolManager>();
        }

        public void BuildComponent(IServiceProvider serviceProvider)
        {
            IProtocolEngine protocolEngine = serviceProvider.GetRequiredService<IProtocolEngine>();
            protocolEngine.Build();
        }
    }
}
