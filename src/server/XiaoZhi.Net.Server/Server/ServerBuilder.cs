using Flurl;
using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
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

        private IKernelBuilder? _kernelBuilder;

        private readonly HostApplicationBuilder _hostApplicationBuilder;

        private ServerBuilder()
        {
            this._hostApplicationBuilder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
            {
                ApplicationName = "XiaoZhi.Net.Server",
#if DEBUG
                EnvironmentName = Environments.Development
#else
                EnvironmentName = Environments.Production
#endif
            });
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
            IServiceCollection services = this._hostApplicationBuilder.Services;

            services.AddSingleton(apiConfig);
            services.AddSingleton<IFlurlClientCache>(_ => new FlurlClientCache()
                .Add("ManageApi", apiConfig.ManageApiUrl, builder =>
                {
                    builder.Headers.Add("authorization", apiConfig.Secret);
                    builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                }));

            ApiResponse<XiaoZhiConfig> res = await apiConfig.ManageApiUrl
                .AppendPathSegment(ApiActions.GetGlobalConfig)
                .WithHeader("authorization", apiConfig.Secret)
                .WithSettings(s =>
                {
                    s.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                })
                .GetJsonAsync<ApiResponse<XiaoZhiConfig>>();

            if (res.Data is null)
            {
                throw new ArgumentNullException(nameof(XiaoZhiConfig), "Failed to get config from remote api.");
            }

            return this.Initialize(res.Data);
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

            this._hostApplicationBuilder.Services.AddSingleton(config);
            this._hostApplicationBuilder.Services.AddSingleton(config.AudioSetting);

            this._hostApplicationBuilder.Services.AddSingleton(connectionStore);

            this._kernelBuilder = this._hostApplicationBuilder.Services.AddKernel();
            this._hostApplicationBuilder.Services.AddTransient<IFunctionInvocationFilter, MCPToolFunctionFilter>();

            LoggerManager.RegisterServices(this._hostApplicationBuilder, config);
            SessionManager.RegisterServices(this._hostApplicationBuilder);
            ProtocolManager.RegisterServices(this._hostApplicationBuilder, config);
            ProviderManager.RegisterServices(this._hostApplicationBuilder, config);
            HandlerManager.RegisterServices(this._hostApplicationBuilder);
            AdvancedManager.RegisterServices(this._hostApplicationBuilder, config);

            this._hostApplicationBuilder.Services.AddHostedService<XiaoZhiEngine>();

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
            if (this._kernelBuilder == null)
            {
                throw new InvalidOperationException("Kernel builder is not initialized. Please call Initialize() first.");
            }
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentNullException(nameof(pluginName), "Plugin name cannot be null or empty.");
            }
            this._kernelBuilder.Plugins.AddFromType<TPlugin>(pluginName);
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
            if (this._kernelBuilder == null)
            {
                throw new InvalidOperationException("Kernel builder is not initialized. Please call Initialize() first.");
            }
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentNullException(nameof(pluginName), "Plugin name cannot be null or empty.");
            }
            if (functions == null || !functions.Any())
            {
                throw new ArgumentNullException(nameof(functions), "Functions cannot be null or empty.");
            }
            IEnumerable<KernelFunction> kernelFunctions = functions.Select(f => KernelFunctionFactory.CreateFromMethod(f.Method, f.FunctionName, f.Description));
            this._kernelBuilder.Plugins.AddFromFunctions(pluginName, kernelFunctions);
            return this;
        }

        public IServerBuilder WithVerify<T>() where T : class, IBasicVerify
        {
            if (this._kernelBuilder == null)
            {
                throw new InvalidOperationException("Kernel builder is not initialized. Please call Initialize() first.");
            }
            this._kernelBuilder.Services.AddSingleton<IBasicVerify, T>();
            return this;
        }

        /// <summary>
        /// 构建服务引擎
        /// </summary>
        /// <returns></returns>
        public IServerEngine Build()
        {
            IHost host = this._hostApplicationBuilder.Build();

            this.BuildComponents(host.Services);

            if (host.Services.GetRequiredService<IHostedService>() is XiaoZhiEngine engine)
            {
                return engine;
            }
            else
            {
                throw new InvalidOperationException("Please initialize the builder first.");
            }
        }

        private void BuildComponents(IServiceProvider serviceProvider)
        {
            ProtocolManager protocolManager = serviceProvider.GetRequiredService<ProtocolManager>();
            ProviderManager providerManager = serviceProvider.GetRequiredService<ProviderManager>();
            HandlerManager handlerManager = serviceProvider.GetRequiredService<HandlerManager>();
            protocolManager.BuildComponent(serviceProvider);
            bool builded = providerManager.BuildComponent(serviceProvider);
        }
    }
}
