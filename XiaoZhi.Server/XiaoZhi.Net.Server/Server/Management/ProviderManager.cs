using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using Serilog;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Plugins;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.ASR;
using XiaoZhi.Net.Server.Providers.AudioCodec;
using XiaoZhi.Net.Server.Providers.LLM;
using XiaoZhi.Net.Server.Providers.Memory;
using XiaoZhi.Net.Server.Providers.Punctuation;
using XiaoZhi.Net.Server.Providers.TTS;
using XiaoZhi.Net.Server.Providers.VAD;

namespace XiaoZhi.Net.Server.Management
{
    internal sealed class ProviderManager
    {
        private readonly ILogger _logger;

        public ProviderManager(ILogger logger)
        {
            this._logger = logger;
        }

        public static void RegisterServices(IServiceCollection services, XiaoZhiConfig config)
        {
            services.AddSingleton<IAudioDecoder, DefaultOpusDecoder>();
            RegisterVad(services, config);
            RegisterAsr(services, config);
            RegisterPunctuation(services, config);
            RegisterLlm(services, config);
            RegisterMemory(services, config);
            RegisterTts(services, config);
            services.AddSingleton<IAudioEncoder, DefaultOpusEncoder>();

            services.AddSingleton<ProviderManager>();
        }

        public bool Initialize(IServiceProvider serviceProvider)
        {
            IList<IProvider> providers = new List<IProvider>
            {
                serviceProvider.GetRequiredService<IAudioDecoder>(),
                serviceProvider.GetRequiredService<IAsr>(),
                serviceProvider.GetRequiredService<IVad>(),
                serviceProvider.GetRequiredService<IPunctuation>(),
                serviceProvider.GetRequiredService<IMemory>(),
                serviceProvider.GetRequiredService<ILlm>(),
                serviceProvider.GetRequiredService<ITts>(),
                serviceProvider.GetRequiredService<IAudioEncoder>()
            };

            foreach (IProvider provider in providers)
            {
                if (!provider.Initialize())
                {
                    this._logger.Error($"Failed to initialize {provider.ModelName} provider.");
                    return false;
                }
            }
            return true;
        }

        public void RegisterPlugins<TPlugin>(IServiceProvider serviceProvider, string pluginName)
        {
            Kernel kernel = serviceProvider.GetRequiredService<Kernel>();

            var plugin = KernelPluginFactory.CreateFromType<TPlugin>(pluginName);
            kernel.Plugins.Add(plugin);
        }

        public void RegisterPlugins(IServiceProvider serviceProvider, string pluginName, IEnumerable<IFunction> functions)
        {
            Kernel kernel = serviceProvider.GetRequiredService<Kernel>();
            var plugin = new MutableKernelPlugin(pluginName);

            foreach (IFunction function in functions)
            {
                plugin.AddFunction(KernelFunctionFactory.CreateFromMethod(function.Method, function.FunctionName, function.Description));
            }

            kernel.Plugins.Add(plugin);
        }

        public void Dispose(IServiceProvider serviceProvider)
        {
            IList<IProvider> providers = new List<IProvider>
            {
                serviceProvider.GetRequiredService<IAudioDecoder>(),
                serviceProvider.GetRequiredService<IAsr>(),
                serviceProvider.GetRequiredService<IVad>(),
                serviceProvider.GetRequiredService<IPunctuation>(),
                serviceProvider.GetRequiredService<IMemory>(),
                serviceProvider.GetRequiredService<ILlm>(),
                serviceProvider.GetRequiredService<ITts>(),
                serviceProvider.GetRequiredService<IAudioEncoder>()
            };

            foreach (IProvider provider in providers)
            {
                provider.Dispose();
            }
        }

        #region Register providers
        private static void RegisterVad(IServiceCollection services, XiaoZhiConfig config)
        {
            switch (config.VadSetting.ModelName.ToLower())
            {
                case "silero":
                    services.AddSingleton<IVad, Silero>(); break;
                case "webrtc":
                    services.AddSingleton<IVad, WebRtc>(); break;
                default:
                    throw new ModelInitializeException("Invalid vad model.");
            }
        }

        private static void RegisterAsr(IServiceCollection services, XiaoZhiConfig config)
        {
            switch (config.AsrSetting.ModelName.ToLower())
            {
                case "sense-voice":
                    services.AddSingleton<IAsr, SenseVoice>(); break;
                case "paraformer":
                    services.AddSingleton<IAsr, Paraformer>(); break;
                default:
                    throw new ModelInitializeException("Invalid asr model.");
            }
        }

        private static void RegisterPunctuation(IServiceCollection services, XiaoZhiConfig config)
        {
            switch (config.PunctuationSetting.ModelName.ToLower())
            {
                case "ct-transformer":
                    services.AddSingleton<IPunctuation, CtTransformer>(); break;
                default:
                    throw new ModelInitializeException("Invalid punctuation model.");
            }
        }

        private static void RegisterLlm(IServiceCollection services, XiaoZhiConfig config)
        {
            try
            {
                string endPoint = config.LlmSetting.Config.BaseUrl;
                string apiKey = config.LlmSetting.Config.ApiKey;
                string modelId = config.LlmSetting.Config.ModelName;
                services.AddKeyedSingleton<IChatCompletionService>(GenericOpenAI.SERVICE_ID, (sp, key) =>
                {

                    OpenAIClientOptions options = new OpenAIClientOptions
                    {
                        Endpoint = new Uri(endPoint),
                        ProjectId = "Xiao Zhi Test"
                    };
                    OpenAIClient openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);

                    return new OpenAIChatCompletionService(modelId, openAIClient);
                });

                switch (config.LlmSetting.ModelName.ToLower())
                {
                    case "qwen":
                    case "doubao":
                    case "deepseek":
                    case "chatglm":
                        services.AddSingleton<ILlm, GenericOpenAI>(); break;
                    default:
                        throw new ModelInitializeException("Invalid llm model.");
                }
            }
            catch (Exception)
            {

                throw;
            }

        }

        private static void RegisterMemory(IServiceCollection services, XiaoZhiConfig config)
        {
            switch (config.MemorySetting.ModelName.ToLower())
            {
                case "flash-memory":
                    services.AddSingleton<IMemory, FlashMemory>(); break;
                case "database":
                    services.AddSingleton<IMemory, Database>(); break;
                default:
                    throw new ModelInitializeException("Invalid memory model.");
            }
        }

        private static void RegisterTts(IServiceCollection services, XiaoZhiConfig config)
        {
            switch (config.TtsSetting.ModelName.ToLower())
            {
                case "kokoro":
                    services.AddSingleton<ITts, Kokoro>(); break;
                default:
                    throw new ModelInitializeException("Invalid tts model.");
            }
        }
        #endregion
    }
}
