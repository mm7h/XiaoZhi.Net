using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server
{
    internal sealed class ServerEngine : IServerEngine
    {
        private IServiceProvider _serviceProvider;

        internal ServerEngine(IServiceProvider serviceProvider)
        {
            this._serviceProvider = serviceProvider;
        }

        public bool Started => this._serviceProvider?.GetService<IProtocolEngine>()?.Started ?? false;

        public IAdvanced Advanced => this._serviceProvider?.GetRequiredService<IAdvanced>() ?? throw new InvalidOperationException("Please build the server engine first.");


        public async Task StartAsync()
        {
            IProtocolEngine protocolEngine = this._serviceProvider.GetRequiredService<IProtocolEngine>();

            await protocolEngine.StartAsync();
        }

        public async Task StopAsync()
        {
            IProtocolEngine protocolEngine = this._serviceProvider.GetRequiredService<IProtocolEngine>();
            ProviderManager providerManager = this._serviceProvider.GetRequiredService<ProviderManager>();
            HandlerManager handlerManager = this._serviceProvider.GetRequiredService<HandlerManager>();
            providerManager.Dispose(this._serviceProvider);
            handlerManager.Dispose(this._serviceProvider);


            await protocolEngine.StopAsync();
            Log.CloseAndFlush();
        }

    }
}
