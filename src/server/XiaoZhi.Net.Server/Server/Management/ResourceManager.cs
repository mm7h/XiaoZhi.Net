using System;
using System.ClientModel;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Resources;
using XiaoZhi.Net.Server.Resources.AudioCaching;
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
                services.AddSingleton<IAudioFileCaching, AudioFileCaching>();
                services.AddSingleton<IMusicFileProvider, MusicProvider>();
                services.AddSingleton<IVadOnnxModel, SileroOnnx>();

                RegisterRag(services, config);

                services.AddSingleton<ResourceManager>();
            });
        }

        public override bool BuildComponent()
        {
            #region AudioCaching
            IAudioFileCaching audioFileCaching = this.ServiceProvider.GetRequiredService<IAudioFileCaching>();
            if (!audioFileCaching.Load(this.Config.DeviceBindSetting))
            {
                return false;
            }
            #endregion

            #region Musics
            IMusicFileProvider musics = this.ServiceProvider.GetRequiredService<IMusicFileProvider>();
            if (!musics.Load(this.Config.MusicSetting))
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
            IRag? rag = this.ServiceProvider.GetService<IRag>();
            if (rag is not null && !rag.Load(this.GetSelectedSetting("RAG", this.Config)))
            {
                return false;
            }
            #endregion

            return true;
        }

        private static void RegisterRag(IServiceCollection services, XiaoZhiConfig config)
        {
            if (!HasSelectedRagSetting(config))
            {
                return;
            }

            foreach (var ragSettingItem in config.ConfiguredSettings["RAG"])
            {
                string? endPoint = ragSettingItem.Value.GetConfigValueOrDefault("BaseUrl");
                string? apiKey = ragSettingItem.Value.GetConfigValueOrDefault("ApiKey");
                string? modelId = ragSettingItem.Value.GetConfigValueOrDefault("ModelName");

                if (string.IsNullOrWhiteSpace(endPoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(modelId))
                {
                    throw new ModelBuildException($"RAG 模型配置“{ragSettingItem.Key}”缺少 BaseUrl、ApiKey 或 ModelName。");
                }

                string llmProviderKey = $"RAG_LLM_{ragSettingItem.Key}";

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

        private static bool HasSelectedRagSetting(XiaoZhiConfig config)
        {
            if (!config.SelectedSettings.TryGetValue("RAG", out string? selectedRag)
                || string.IsNullOrWhiteSpace(selectedRag)
                || !config.ConfiguredSettings.TryGetValue("RAG", out Dictionary<string, Dictionary<string, string>>? ragSettings))
            {
                return false;
            }

            return ragSettings.ContainsKey(selectedRag);
        }
    }
}
