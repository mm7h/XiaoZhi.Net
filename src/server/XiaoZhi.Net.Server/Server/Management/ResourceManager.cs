using System;
using System.ClientModel;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Resources;
using XiaoZhi.Net.Server.Resources.DeviceBinding;
using XiaoZhi.Net.Server.Resources.Musics;
using XiaoZhi.Net.Server.Resources.OnnxModels;
using XiaoZhi.Net.Server.Resources.OnnxModels.VAD;
using XiaoZhi.Net.Server.Resources.Rag;

namespace XiaoZhi.Net.Server.Management
{
    internal class ResourceManager : BaseManager
    {
        public ResourceManager(IServiceProvider serviceProvider, XiaoZhiConfig config, ILogger<ResourceManager> logger) : base(serviceProvider, config, logger)
        {
        }

        public static IHostBuilder RegisterServices(IHostBuilder builder, XiaoZhiConfig config)
        {
            return builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IDeviceBinding, DefaultDeviceBinding>();
                services.AddSingleton<IMusicFileProvider, MusicProvider>();
                services.AddSingleton<IVadOnnxModel, SileroOnnx>();

                RegisterRag(services, config);

                services.AddSingleton<ResourceManager>();
            });
        }

        public override bool BuildComponent()
        {
            #region DeviceBinding
            IDeviceBinding deviceBinding = this.ServiceProvider.GetRequiredService<IDeviceBinding>();
            if (!deviceBinding.Load(this.Config.DeviceBindSetting))
            {
                return false;
            }
            #endregion

            #region Musics
            IMusicFileProvider musics = this.ServiceProvider.GetRequiredService<IMusicFileProvider>();
            if (!musics.Load(this.Config.MusicProviderSetting))
            {
                return false;
            }
            #endregion

            #region Onnx models
            IVadOnnxModel vadOnnxModel = this.ServiceProvider.GetRequiredService<IVadOnnxModel>();
            if (!vadOnnxModel.Load(this.GetSelectedSetting("VAD", this.Config)))
            {
                return false;
            }
            #endregion

            #region RAG
            IRag rag = this.ServiceProvider.GetRequiredService<IRag>();
            if (!rag.Load(this.GetSelectedSetting("RAG", this.Config)))
            {
                return false;
            }
            #endregion

            return true;
        }

        public override void Dispose()
        {
            IList<IDisposable> resources = new List<IDisposable>
            {
                this.ServiceProvider.GetRequiredService<IDeviceBinding>(),
                this.ServiceProvider.GetRequiredService<IMusicFileProvider>(),
                this.ServiceProvider.GetRequiredService<IVadOnnxModel>(),
                this.ServiceProvider.GetRequiredService<IRag>()
            };

            foreach (IDisposable resource in resources)
            {
                resource.Dispose();
            }
        }

        private static void RegisterRag(IServiceCollection services, XiaoZhiConfig config)
        {
            foreach (var llmSettingItem in config.ConfiguredSettings["LLM"])
            {
                string? type = llmSettingItem.Value.GetConfigValueOrDefault("Type");
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }
                if (!type.Equals(GlobalVariables.RagAgentType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string? endPoint = llmSettingItem.Value.GetConfigValueOrDefault("BaseUrl");
                string? apiKey = llmSettingItem.Value.GetConfigValueOrDefault("ApiKey");
                string? modelId = llmSettingItem.Value.GetConfigValueOrDefault("ModelName");

                if (string.IsNullOrWhiteSpace(endPoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(modelId))
                {
                    throw new ModelBuildException($"Invalid llm model setting, endPoint: {endPoint}, apiKey: {apiKey}, modelId: {modelId}.");
                }

                string llmProviderKey = $"RAG_LLM_{llmSettingItem.Key}";

                services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(llmProviderKey, (_, _) =>
                {
                    OpenAIClient openAIClient = new OpenAIClient(
                        new ApiKeyCredential(apiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(endPoint) });
                    return openAIClient.GetEmbeddingClient(modelId).AsIEmbeddingGenerator();
                });

            }
            services.AddSingleton<IRag, DefaultRag>();
            
        }
    }
}
