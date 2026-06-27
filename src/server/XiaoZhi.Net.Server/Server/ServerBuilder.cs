using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Globalization;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.FunctionTools;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Services;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server
{
    internal class ServerBuilder : IServerBuilder
    {
        private static readonly Lazy<IServerBuilder> lazyInstance = new Lazy<IServerBuilder>(() => new ServerBuilder());

        // 记录所有注册的工具类型（Type, IsPrivate）
        private readonly List<(Type Type, bool IsPrivate)> _functionToolRegistrations = [];

        private ServerBuilder()
        {
            this.HostBuilder = Host.CreateDefaultBuilder();
        }

        internal ServerBuilder(IHostBuilder hostBuilder)
        {
            this.HostBuilder = hostBuilder;
        }

        public static IServerBuilder CreateServerBuilder() => lazyInstance.Value;
        public static IServerBuilder CreateServerBuilder(IHostBuilder hostBuilder) => new ServerBuilder(hostBuilder);

        public IHostBuilder HostBuilder { get; private set; }

        public IServerBuilder Initialize(XiaoZhiConfig config)
        {
            return this.Initialize(config, DefaultMemoryStore.Default);
        }

        public IServerBuilder Initialize(XiaoZhiConfig config, IStore connectionStore)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config), Lang.ServerBuilder_Initialize_ConfigNull);
            }
            this.HostBuilder = this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton(config);

                services.AddSingleton(connectionStore);
            })
            .RegisterLogger(config)
            .RegisterResources()
            .RegisterProviders(config)
            .RegisterHandlers()
            .RegisterObjectPools()
            .RegisterProtocol(config)
            .RegisterFunctionTools();

#if DEBUG
            this.HostBuilder.UseEnvironment("Development");
#else
            this.HostBuilder.UseEnvironment("Production");
#endif

            return this;
        }

        public IServerBuilder WithFunctionTools<TFunctionTool>() where TFunctionTool : class, IFunctionTool
        {
            this._functionToolRegistrations.Add((typeof(TFunctionTool), false));
            this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IFunctionTool, TFunctionTool>();
            });
            
            return this;
        }

        public IServerBuilder WithPrivateFunctionTools<TFunctionTool>() where TFunctionTool : class, IPrivateFunctionTool
        {
            this._functionToolRegistrations.Add((typeof(TFunctionTool), true));
            this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddTransient<IPrivateFunctionTool, TFunctionTool>();
            });

            return this;
        }

        public IServerBuilder WithVerify<T>() where T : class, IBasicVerify
        {
            this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IBasicVerify, T>();
            });

            return this;
        }

        public IServerBuilder WithManageApi(string manageApiUrl, string secret)
        {
            this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IFlurlClientCache>(_ => new FlurlClientCache()
                .Add("ManageApi", manageApiUrl, builder =>
                {
                    builder.WithOAuthBearerToken(secret);   
                    builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                }));
                services.AddSingleton<ManageApiClient>();
            });
            return this;
        }

        public IServerBuilder WithCulture(string culture = "zh-CN")
        {
            if (!string.IsNullOrEmpty(culture))
            {
                CultureInfo cultureInfo = new CultureInfo(culture);
                Lang.Culture = cultureInfo;
            }
            else
            {
                Lang.Culture = CultureInfo.CurrentCulture;
            }
           
            return this;
        }

        public IServerBuilder WithCulture(CultureInfo culture)
        {
            if (culture is not null)
            {
                Lang.Culture = culture;
            }
            else
            {
                Lang.Culture = CultureInfo.CurrentCulture;
            }

            return this;
        }
        public IHost Build()
        {
            IHost host = this.HostBuilder.Build();

            this.BuildComponents(host.Services);

            return host;
        }

        private void BuildComponents(IServiceProvider serviceProvider)
        {
            ResourceManager resourceManager = serviceProvider.GetRequiredService<ResourceManager>();
            ProviderManager providerManager = serviceProvider.GetRequiredService<ProviderManager>();
            bool loaded = resourceManager.BuildComponent(serviceProvider);
            if (!loaded)
            {
                Serilog.Log.CloseAndFlush();
                throw new ApplicationException(Lang.ServerBuilder_BuildComponents_ResourceLoadFailed);
            }
            bool builded = providerManager.BuildComponent(serviceProvider);
            if (!builded)
            {
                Serilog.Log.CloseAndFlush();
                throw new ApplicationException(Lang.ServerBuilder_BuildComponents_ProviderBuildFailed);
            }

            // 初始化 FunctionToolManager
            FunctionToolManager functionToolManager = serviceProvider.GetRequiredService<FunctionToolManager>();
            foreach (var (type, isPrivate) in this._functionToolRegistrations)
            {
                functionToolManager.RegisterToolType(type, isPrivate);
            }
            bool toolsLoaded = functionToolManager.BuildComponent(serviceProvider);
            if (!toolsLoaded)
            {
                Serilog.Log.CloseAndFlush();
                throw new ApplicationException("Function tool initialization failed.");
            }
        }
    }
}
