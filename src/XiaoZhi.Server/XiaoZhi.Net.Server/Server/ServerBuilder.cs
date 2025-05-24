using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Server
{
    internal class ServerBuilder : IServerBuilder
    {
        private static readonly Lazy<IServerBuilder> lazyInstance = new Lazy<IServerBuilder>(() => new ServerBuilder());
        internal static IServerBuilder CreateServerBuilder() => lazyInstance.Value;

        private readonly IKernelBuilder _kernelBuilder;

        private ServerBuilder()
        {
            _kernelBuilder = Kernel.CreateBuilder();
        }

        /// <summary>
        /// 初始化服务
        /// </summary>
        /// <param name="config">配置信息</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public IServerBuilder Initialize(XiaoZhiConfig config)
        {
            this.Initialize(config, DefaultMemoryStore.Default);
            return this;
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

            IServiceCollection services = _kernelBuilder.Services;

            services.AddSingleton(config);
            services.AddSingleton(config.LogSetting);
            services.AddSingleton(config.AudioSetting);

            services.AddSingleton(connectionStore);

            LoggerManager.RegisterServices(services, config);
            ProtocolManager.RegisterServices(services, config);
            ProviderManager.RegisterServices(services, config);
            HandlerManager.RegisterServices(services, config);
            AdvancedManager.RegisterServices(services, config);

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
            _kernelBuilder.Plugins.AddFromType<TPlugin>(pluginName);
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
            _kernelBuilder.Plugins.AddFromFunctions(pluginName, kernelFunctions);
            return this;
        }

        public IServerBuilder WithVerify<T>() where T : class, IBasicVerify
        {
            _kernelBuilder.Services.AddSingleton<IBasicVerify, T>();
            return this;
        }

        /// <summary>
        /// 构建服务引擎
        /// </summary>
        /// <returns></returns>
        public IServerEngine Build()
        {
            Kernel kernel = _kernelBuilder.Build();

            _kernelBuilder.Services.AddSingleton(kernel);
            IServiceProvider serviceProvider = _kernelBuilder.Services.BuildServiceProvider();

            this.BuildComponents(serviceProvider);

            return new ServerEngine(serviceProvider);
        }

        private void BuildComponents(IServiceProvider serviceProvider)
        {
            ProtocolManager protocolManager = serviceProvider.GetRequiredService<ProtocolManager>();
            ProviderManager providerManager = serviceProvider.GetRequiredService<ProviderManager>();
            HandlerManager handlerManager = serviceProvider.GetRequiredService<HandlerManager>();
            protocolManager.BuildComponent(serviceProvider);
            bool builded = providerManager.BuildComponent(serviceProvider);
            if (builded)
            {
                handlerManager.BuildComponent(serviceProvider);
            }
        }
    }
}
