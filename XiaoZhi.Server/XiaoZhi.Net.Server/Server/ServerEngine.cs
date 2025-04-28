using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server
{
    internal sealed class ServerEngine : IServerEngine
    {
        private static readonly Lazy<ServerEngine> lazyInstance = new Lazy<ServerEngine>(() => new ServerEngine());
        internal static IServerEngine Create() => lazyInstance.Value;

        private IServiceProvider _serviceProvider;

        private ServerEngine()
        {

        }
        public bool Started => this._serviceProvider?.GetService<IProtocolEngine>()?.Started ?? false;

        public IStore ConnectionStore { get; set; }
        public IAdvanced Advanced => this._serviceProvider?.GetRequiredService<IAdvanced>() ?? throw new InvalidOperationException("Please initialize the server engine first.");



        public IServerEngine Initialize(XiaoZhiConfig config)
        {
            if (config == null)
            {
                throw new InvalidDataException("Config is null.");
            }
            try
            {
                this._serviceProvider = this.RegisterServices(config);
                this.InitializeManagers(this._serviceProvider);
                return this;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, $"Failed to initialize server engine: {ex.Message}.");
                Log.Error("Failed to initialize server engine.");
                Log.CloseAndFlush();
                throw ex;
            }
        }

        public IServerEngine WithPlugin<TPlugin>(string pluginName)
        {
            ProviderManager providerManager = this._serviceProvider.GetRequiredService<ProviderManager>();
            providerManager.RegisterPlugins<TPlugin>(this._serviceProvider, pluginName);
            return this;
        }

        public IServerEngine WithPlugin(string pluginName, IEnumerable<IFunction> functions)
        {
            // not test yet
            ProviderManager providerManager = this._serviceProvider.GetRequiredService<ProviderManager>();
            providerManager.RegisterPlugins(this._serviceProvider, pluginName, functions);
            return this;
        }

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

        private IServiceProvider RegisterServices(XiaoZhiConfig config)
        {
            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            IServiceCollection services = kernelBuilder.Services;

            services.AddSingleton(config);
            services.AddSingleton(config.LogSetting);

            if (this.ConnectionStore != null)
            {
                services.AddSingleton(this.ConnectionStore);
            }
            else
            {
                services.AddSingleton<IStore, DefaultMemoryStore>();
            }
            services.AddSingleton(config.AudioSetting);

            LoggerManager.RegisterServices(services, config);
            ProtocolManager.RegisterServices(services, config);
            ProviderManager.RegisterServices(services, config);
            HandlerManager.RegisterServices(services, config);
            AdvancedManager.RegisterServices(services, config);

            Kernel kernel = kernelBuilder.Build();
            services.AddSingleton(kernel);

            return services.BuildServiceProvider();
        }

        private void InitializeManagers(IServiceProvider serviceProvider)
        {
            ProtocolManager protocolManager = this._serviceProvider.GetRequiredService<ProtocolManager>();
            ProviderManager providerManager = this._serviceProvider.GetRequiredService<ProviderManager>();
            HandlerManager handlerManager = this._serviceProvider.GetRequiredService<HandlerManager>();
            protocolManager.Initialize(serviceProvider);
            bool initialized = providerManager.Initialize(serviceProvider);
            if (initialized)
            {
                handlerManager.Initialize(serviceProvider);
            }
        }
    }
}
