using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server
{
    internal sealed class XiaoZhiEngine : IServerEngine, IHostedService
    {
        private IHost _host;

        public XiaoZhiEngine(IHost host)
        {
            this._host = host;
        }

        public bool Started => this._host.Services.GetService<IProtocolEngine>()?.Started ?? false;

        public IAdvanced Advanced => this._host.Services.GetRequiredService<IAdvanced>() ?? throw new InvalidOperationException("Please build the server engine first.");

        #region Engine Start / Stop
        public Task StartAsync() => this._host.RunAsync();
        public Task StopAsync() => this._host.StopAsync(); 
        #endregion

        #region Hosted Start / Stop
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            IProtocolEngine protocolEngine = this._host.Services.GetRequiredService<IProtocolEngine>();

            await protocolEngine.StartAsync();
        }


        public async Task StopAsync(CancellationToken cancellationToken)
        {
            IProtocolEngine protocolEngine = this._host.Services.GetRequiredService<IProtocolEngine>();
            ProviderManager providerManager = this._host.Services.GetRequiredService<ProviderManager>();
            providerManager.Dispose(this._host.Services);

            await protocolEngine.StopAsync();
            Serilog.Log.CloseAndFlush();
        } 
        #endregion
    }
}
