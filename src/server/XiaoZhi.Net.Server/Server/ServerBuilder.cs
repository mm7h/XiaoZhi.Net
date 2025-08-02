using Flurl;
using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Services;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server
{
    internal class ServerBuilder : IServerBuilder
    {
        private static readonly Lazy<IServerBuilder> lazyInstance = new Lazy<IServerBuilder>(() => new ServerBuilder());
        internal static IServerBuilder CreateServerBuilder() => lazyInstance.Value;

        private IHostBuilder _hostBuilder;

        private ServerBuilder()
        {
            this._hostBuilder = Host.CreateDefaultBuilder();
        }

        internal ServerBuilder(IHostBuilder hostBuilder)
        {
            this._hostBuilder = hostBuilder;
        }

        public static IServerBuilder CreateServerBuilder(IHostBuilder hostBuilder)
        { 
            return new ServerBuilder(hostBuilder);
        }

        /// <summary>
        /// 通过Remote API初始化服务
        /// </summary>
        /// <param name="apiConfig"></param>
        /// <returns></returns>
        public async Task<IServerBuilder> Initialize(XiaoZhiApiConfig apiConfig)
        {
            return await this.Initialize(apiConfig, DefaultMemoryStore.Default);
        }

        /// <summary>
        /// 通过Remote API初始化服务
        /// </summary>
        /// <param name="apiConfig"></param>
        /// <param name="connectionStore"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task<IServerBuilder> Initialize(XiaoZhiApiConfig apiConfig, IStore connectionStore)
        {
            try
            {
                ApiResponse<XiaoZhiConfig> res = await apiConfig.ManageApiUrl
                    .AppendPathSegment(ApiActions.GetGlobalConfig)
                    .WithHeader("authorization", apiConfig.Secret)
                    .WithSettings(s =>
                    {
                        s.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                    })
                    .GetJsonAsync<ApiResponse<XiaoZhiConfig>>();


                this._hostBuilder.ConfigureServices((context, services) =>
                {
                    services.AddSingleton(context.HostingEnvironment);
                    services.AddSingleton(context.Configuration);

                    services.AddSingleton(apiConfig);
                    services.AddSingleton<IFlurlClientCache>(_ => new FlurlClientCache()
                        .Add("ManageApi", apiConfig.ManageApiUrl, builder =>
                        {
                            builder.Headers.Add("authorization", apiConfig.Secret);
                            builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                        }));

                    services.AddSingleton<XiaoZhiApiConfig>(apiConfig);
                    services.AddSingleton<ManageApiClient>();
                });

                if (res.Data is null)
                {
                    throw new ArgumentNullException(nameof(XiaoZhiConfig), "Failed to get config from remote api.");
                }


                return this.Initialize(res.Data);
            }
            catch (Exception)
            {

                throw;
            }
        }

        /// <summary>
        /// 初始化服务
        /// </summary>
        /// <param name="config">配置信息</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public IServerBuilder Initialize(XiaoZhiConfig config)
        {
            return this.Initialize(config, DefaultMemoryStore.Default);
        }

        /// <summary>
        /// 初始化服务
        /// </summary>
        /// <param name="config">配置信息</param>
        /// <param name="connectionStore">自定义的连接信息存储管理器</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public IServerBuilder Initialize(XiaoZhiConfig config, IStore connectionStore)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config), "Config cannot be null.");
            }
            this._hostBuilder = this._hostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton(config);
                services.AddSingleton(config.AudioSetting);

                services.AddSingleton(connectionStore);

                services.AddKernel();
                services.AddTransient<IFunctionInvocationFilter, MCPToolFunctionFilter>();
            })
            .RegisterLogger(config)
            .RegisterProviders(config)
            .RegisterHandlers()
            .RegisterProtocol(config);

#if DEBUG
            this._hostBuilder.UseEnvironment("Development");
#else
            this._hostBuilder.UseEnvironment("Production");
#endif

            return this;
        }

        /// <summary>
        /// 添加插件
        /// </summary>
        /// <typeparam name="TPlugin">插件类对应的Type</typeparam>
        /// <param name="pluginName">插件名称</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public IServerBuilder WithPlugin<TPlugin>(string pluginName)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentNullException(nameof(pluginName), "Plugin name cannot be null or empty.");
            }
            this._hostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<KernelPlugin>(sp => KernelPluginFactory.CreateFromType<TPlugin>(pluginName, sp));
            });
            return this;
        }

        /// <summary>
        /// 添加插件
        /// </summary>
        /// <typeparam name="TPlugin">插件类对应的Type</typeparam>
        /// <param name="pluginName">插件名称</param>
        /// <param name="functions">支撑该插件的functions</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public IServerBuilder WithPlugin<TPlugin>(string pluginName, IEnumerable<IFunction> functions)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentNullException(nameof(pluginName), "Plugin name cannot be null or empty.");
            }
            if (functions == null || !functions.Any())
            {
                throw new ArgumentNullException(nameof(functions), "Functions cannot be null or empty.");
            }

            IEnumerable<KernelFunction> kernelFunctions = functions.Select(f => KernelFunctionFactory.CreateFromMethod(f.Method, f.FunctionName, f.Description));
            this._hostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<KernelPlugin>(sp => KernelPluginFactory.CreateFromFunctions(pluginName, kernelFunctions));
            });
            return this;
        }

        public IServerBuilder WithVerify<T>() where T : class, IBasicVerify
        {
            this._hostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IBasicVerify, T>();
            });

            return this;
        }

        /// <summary>
        /// 构建服务引擎
        /// </summary>
        /// <returns></returns>
        public IHost Build()
        {
            IHost host = this._hostBuilder.Build();

            this.BuildComponents(host.Services);

            return host;
        }

        private void BuildComponents(IServiceProvider serviceProvider)
        {
            ProviderManager providerManager = serviceProvider.GetRequiredService<ProviderManager>();
            HandlerManager handlerManager = serviceProvider.GetRequiredService<HandlerManager>();
            bool builded = providerManager.BuildComponent(serviceProvider);
            if (!builded)
            {
                throw new ApplicationException("Failed to build provider components. Please check the configuration and provider implementations.");
            }
        }
    }
}
