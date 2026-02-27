using Flurl.Http.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.ASR.Sherpa;
using XiaoZhi.Net.Server.Providers.AudioCodec;
using XiaoZhi.Net.Server.Providers.AudioMixer;
using XiaoZhi.Net.Server.Providers.AudioPlayer;
using XiaoZhi.Net.Server.Providers.AudioPlayer.Music;
using XiaoZhi.Net.Server.Providers.AudioPlayer.SystemNotification;
using XiaoZhi.Net.Server.Providers.IoT;
using XiaoZhi.Net.Server.Providers.LLM;
using XiaoZhi.Net.Server.Providers.LLM.Agents;
using XiaoZhi.Net.Server.Providers.LLM.FunctionInvocationFilters;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;
using XiaoZhi.Net.Server.Providers.MCP;
using XiaoZhi.Net.Server.Providers.MCP.DeviceMcp;
using XiaoZhi.Net.Server.Providers.MCP.McpEndpoint;
using XiaoZhi.Net.Server.Providers.MCP.ServerMcp;
using XiaoZhi.Net.Server.Providers.Memory;
using XiaoZhi.Net.Server.Providers.TTS;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan;
using XiaoZhi.Net.Server.Providers.TTS.Sherpa;
using XiaoZhi.Net.Server.Providers.VAD.Native;
using XiaoZhi.Net.Server.Providers.VAD.Sherpa;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Services;

namespace XiaoZhi.Net.Server.Management
{
    internal class ProviderManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Kernel _globalKernel;
        private readonly XiaoZhiConfig _config;
        private readonly ILogger<ProviderManager> _logger;


        public ProviderManager(IServiceProvider serviceProvider, Kernel _globalKernel, XiaoZhiConfig config, ILogger<ProviderManager> logger)
        {
            this._serviceProvider = serviceProvider;
            this._globalKernel = _globalKernel;
            this._config = config;
            this._logger = logger;
        }

        public static IHostBuilder RegisterServices(IHostBuilder builder, XiaoZhiConfig config)
        {
            return builder.ConfigureServices((context, services) =>
            {
                RegisterAudioDecoder(services);
                RegisterVad(services, config, GlobalProviderNames.GLOBAL_VAD);
                RegisterAsr(services, config, GlobalProviderNames.GLOBAL_ASR);
                RegisterLlm(services, config);
                RegisterMemory(services, config, GlobalProviderNames.GLOBAL_MEMORY);
                RegisterTts(services, config, GlobalProviderNames.GLOBAL_TTS);

                RegisterAudioEncoder(services);
                RegisterAudioResampler(services);
                RegisterAudioMixer(services);
                RegisterIoT(services);
                RegisterMCP(services);
                RegisterLLMPlugins(services);
                RegisterAudioPlayer(services);

                services.AddSingleton<ProviderManager>();
            });
        }

        public bool BuildComponent(IServiceProvider serviceProvider)
        {
            try
            {
                #region Vad
                IVad vad = serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD);
                if (vad.IsSherpaModel && !vad.Build(this.GetSelectedSetting("VAD", this._config)))
                {
                    this._logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, vad.ModelName);
                    return false;
                }
                #endregion

                #region Asr
                IAsr asr = serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR);
                if (asr.IsSherpaModel && !asr.Build(this.GetSelectedSetting("ASR", this._config)))
                {
                    this._logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, asr.ModelName);
                    return false;
                }
                #endregion

                #region Memory
                IMemory memory = serviceProvider.GetRequiredKeyedService<IMemory>(GlobalProviderNames.GLOBAL_MEMORY);
                if (!memory.Build(this.GetSelectedSetting("MEMORY", this._config)))
                {
                    this._logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, memory.ModelName);
                    return false;
                }
                #endregion

                #region Tts
                ITts tts = serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS);
                if (tts.IsSherpaModel && !tts.Build(this.GetSelectedSetting("TTS", this._config)))
                {
                    this._logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, tts.ModelName);
                    return false;
                }
                #endregion

                #region FFmpeg
                if (MediaFactory.CheckFFmpegInstalled(out string ffmpegVersion))
                {
                    this._logger.LogInformation(Lang.ProviderManager_BuildComponent_FFmpegInstalled, ffmpegVersion);
                }
                else
                {
                    this._logger.LogWarning(Lang.ProviderManager_BuildComponent_FFmpegNotFound);
                    return false;
                }
                #endregion

                return true;
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, Lang.ProviderManager_BuildComponent_BuildComponentsFailed);
                return false;
            }
        }

        private ModelSetting GetSelectedSetting(string selectedModelType, XiaoZhiConfig config)
        {
            string selectedModel = config.SelectedSettings[selectedModelType];
            Dictionary<string, string> setting = config.ConfiguredSettings[selectedModelType][selectedModel];

            ModelSetting modelSetting = new ModelSetting
            {
                ModelName = selectedModel,
                Config = setting
            };

            return modelSetting;
        }

        private ModelSetting GetSelectedLLMSetting(string selectedLLMType, XiaoZhiConfig config)
        {
            string selectedModel = config.SelectedSettings[selectedLLMType];
            Dictionary<string, string> setting = config.ConfiguredSettings["LLM"][selectedModel];

            ModelSetting modelSetting = new ModelSetting
            {
                ModelName = selectedModel,
                Config = setting
            };

            return modelSetting;
        }

        public async Task<bool> InitializePrivateConfigAsync(Session session)
        {
            try
            {
                ManageApiClient? manageApiClient = this._serviceProvider.GetService<ManageApiClient>();

                if (manageApiClient is null)
                {
                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_RemoteServiceUnavailable, session.DeviceId);

                    return this.RegisterGlobalProviders(session);
                }

                PrivateModelsConfig? privateModelsConfig = await manageApiClient.LoadConfigFromApi(session.DeviceId, session.SessionId);

                if (privateModelsConfig is null)
                {
                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_NoPrivateConfig, session.DeviceId);

                    return this.RegisterGlobalProviders(session);
                }

                if (privateModelsConfig.VadSetting is not null)
                {
                    IVad privateVad = this._serviceProvider.GetRequiredKeyedService<IVad>(privateModelsConfig.VadSetting.ModelName);
                    if (!privateVad.Build(privateModelsConfig.VadSetting))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateVadBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetVad(privateVad);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateVadInitialized, privateModelsConfig.VadSetting.ModelName, session.DeviceId);
                }
                else
                {
                    IVad genericVad = this._serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD);
                    if (!genericVad.IsSherpaModel && !genericVad.Build(this.GetSelectedSetting("VAD", this._config)))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_GenericVadBuildFailed, genericVad.ModelName);
                        return false;
                    }
                    session.PrivateProvider.SetVad(genericVad);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_GenericVadInitialized, genericVad.ModelName, session.DeviceId);
                }

                if (privateModelsConfig.AsrSetting is not null)
                {
                    IAsr privateAsr = this._serviceProvider.GetRequiredKeyedService<IAsr>(privateModelsConfig.AsrSetting.ModelName);
                    if (!privateAsr.Build(privateModelsConfig.AsrSetting))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateAsrBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetAsr(privateAsr);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateAsrInitialized, privateModelsConfig.AsrSetting.ModelName, session.DeviceId);
                }
                else
                {
                    IAsr genericAsr = this._serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR);
                    if (!genericAsr.IsSherpaModel && !genericAsr.Build(this.GetSelectedSetting("ASR", this._config)))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_GenericAsrBuildFailed, genericAsr.ModelName);
                        return false;
                    }
                    session.PrivateProvider.SetAsr(genericAsr);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_GenericAsrInitialized, genericAsr.ModelName, session.DeviceId);
                }

                if (privateModelsConfig.EmotionLlmSetting is not null && privateModelsConfig.ChatLlmSetting is not null)
                {
                    Kernel privateKernel = this._globalKernel.Clone();
                    ILlm privateLlm = this._serviceProvider.GetRequiredService<ILlm>();

                    string llmModelName = privateModelsConfig.ChatLlmSetting.ModelName;
                    string prompt = privateModelsConfig.ChatLlmSetting.Config.GetConfigValueOrDefault("Prompt", this._config.Prompt);
                    bool useStreaming = privateModelsConfig.ChatLlmSetting.Config.GetConfigValueOrDefault("UseStreaming", false);
                    string summaryMemory = privateModelsConfig.ChatLlmSetting.Config.GetConfigValueOrDefault("SummaryMemory", string.Empty);
                    bool useEmotions = privateModelsConfig.EmotionLlmSetting.Config.GetConfigValueOrDefault("UseEmotions", false);

                    LLMBuildConfig llmBuildConfig = new LLMBuildConfig(
                        privateModelsConfig.EmotionLlmSetting.ModelName,
                        privateModelsConfig.ChatLlmSetting.ModelName,
                        prompt,
                        useStreaming,
                        useEmotions,
                        summaryMemory,
                        privateKernel);

                    privateKernel.Data.Add("session", session);
                    if (!privateLlm.Build(llmBuildConfig))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateLlmBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetKernel(privateKernel);
                    session.PrivateProvider.SetLlm(privateLlm);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateLlmInitialized, privateModelsConfig.EmotionLlmSetting.ModelName, privateModelsConfig.ChatLlmSetting.ModelName, session.DeviceId);
                }
                else
                {
                    Kernel privateKernel = this._globalKernel.Clone();
                    ILlm genericLlm = this._serviceProvider.GetRequiredService<ILlm>();

                    ModelSetting emotionLLMModelSetting = this.GetSelectedLLMSetting("EmotionLLM", this._config);
                    ModelSetting chatLLMModelSetting = this.GetSelectedLLMSetting("ChatLLM", this._config);
                    bool useStreaming = chatLLMModelSetting.Config.GetConfigValueOrDefault("UseStreaming", false);
                    bool useEmotions = emotionLLMModelSetting.Config.GetConfigValueOrDefault("UseEmotions", false);

                    LLMBuildConfig llmBuildConfig = new LLMBuildConfig(
                        emotionLLMModelSetting.ModelName,
                        chatLLMModelSetting.ModelName,
                        this._config.Prompt,
                        useStreaming,
                        useEmotions,
                        SummaryMemory: string.Empty,
                        privateKernel);

                    privateKernel.Data.Add("session", session);
                    if (!genericLlm.Build(llmBuildConfig))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_GenericLlmBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetKernel(privateKernel);
                    session.PrivateProvider.SetLlm(genericLlm);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_GenericLlmInitialized, emotionLLMModelSetting.ModelName, chatLLMModelSetting.ModelName, session.DeviceId);
                }

                if (privateModelsConfig.TtsSetting is not null)
                {
                    ITts privateTts = this._serviceProvider.GetRequiredKeyedService<ITts>(privateModelsConfig.TtsSetting.ModelName);
                    if (!privateTts.Build(privateModelsConfig.TtsSetting))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateTtsBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetTts(privateTts);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateTtsInitialized, privateModelsConfig.TtsSetting.ModelName, session.DeviceId);
                }
                else
                {
                    ITts genericTts = this._serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS);
                    if (!genericTts.IsSherpaModel && !genericTts.Build(this.GetSelectedSetting("TTS", this._config)))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_GenericTtsBuildFailed, genericTts.ModelName);
                        return false;
                    }
                    session.PrivateProvider.SetTts(genericTts);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_GenericTtsInitialized, genericTts.ModelName, session.DeviceId);
                }

                return true;
            }
            catch (DeviceNotFoundException)
            {
                session.IsDeviceBinded = false;
                this._logger.LogWarning(Lang.ProviderManager_InitializePrivateConfig_DeviceNotFound, session.DeviceId);
                return true;
            }
            catch (DeviceBindException deviceBindException)
            {
                session.IsDeviceBinded = false;
                session.BindCode = deviceBindException.BindCode;
                this._logger.LogWarning(Lang.ProviderManager_InitializePrivateConfig_DeviceNotBinded, session.DeviceId, session.BindCode);
                return true;
            }
            catch (Exception ex)
            {
                session.IsDeviceBinded = false;
                this._logger.LogError(ex, Lang.ProviderManager_InitializePrivateConfig_LoadPrivateConfigFailed, session.DeviceId, session.SessionId);
                return false;
            }
            finally
            {
                this.BuildAudioDecoder(session);
                this.BuildAudioPlayer(session);
                this.BuildAudioProcessor(session);
                this.BuildAudioResampler(session);
                this.BuildAudioEncoder(session);
            }
        }

        public async Task SaveMemoryAsync(Session session)
        {
            if (session.PrivateProvider.Llm is null)
            {
                this._logger.LogWarning(Lang.ProviderManager_SaveMemory_LlmNotInitialized, session.DeviceId);
                return;
            }

            if (session.PrivateProvider.Llm.LLMChatHistory.Any())
            {
                ManageApiClient? manageApiClient = this._serviceProvider.GetService<ManageApiClient>();
                if (manageApiClient is not null)
                {
                    try
                    {
                        await manageApiClient.SaveMemoryAsync(session.DeviceId, session.SessionId, session.PrivateProvider.Llm.LLMChatHistory);
                        this._logger.LogInformation(Lang.ProviderManager_SaveMemory_MemorySaved, session.DeviceId, session.SessionId);
                    }
                    catch (Exception ex)
                    {
                        this._logger.LogError(ex, Lang.ProviderManager_SaveMemory_SaveMemoryFailed, session.DeviceId, session.SessionId);
                    }
                }
                else
                {
                    this._logger.LogWarning(Lang.ProviderManager_SaveMemory_ApiClientNotAvailable, session.DeviceId, session.SessionId);
                }
            }
        }

        public void Dispose(IServiceProvider serviceProvider)
        {
            IList<IDisposable> providers = new List<IDisposable>
            {
                serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR),
                serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD),
                serviceProvider.GetRequiredKeyedService<IMemory>(GlobalProviderNames.GLOBAL_MEMORY)
            };

            foreach (IDisposable provider in providers)
            {
                provider.Dispose();
            }

        }

        #region Register providers
        #region AudioDecoder
        private static void RegisterAudioDecoder(IServiceCollection services)
        {
            services.AddTransient<IAudioDecoder, DefaultOpusDecoder>();
        }

        public void BuildAudioDecoder(Session session)
        {
            IAudioDecoder audioDecoder = this._serviceProvider.GetRequiredService<IAudioDecoder>();
            if (!audioDecoder.Build(session.AudioSetting))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioEncoder_BuildFailed, session.SessionId);
            }
            session.PrivateProvider.SetAudioDecoder(audioDecoder);
        }
        #endregion

        #region AudioResampler
        private static void RegisterAudioResampler(IServiceCollection services)
        {
            services.AddTransient<IAudioResampler, DefaultResampler>();
        }
        public void BuildAudioResampler(Session session)
        {
            int ttsSampleRate = session.PrivateProvider.Tts?.GetTtsSampleRate() ?? this._serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS).GetTtsSampleRate();

            if (ttsSampleRate == session.AudioSetting.OutSampleRate)
            {
                return;
            }

            this._logger.LogInformation(Lang.ProviderManager_BuildAudioResampler_ResamplingRequired, session.DeviceId, ttsSampleRate, session.AudioSetting.OutSampleRate);

            ResamplerBuildConfig resamplerBuildConfig = new ResamplerBuildConfig(session.AudioSetting.Channels, ttsSampleRate, session.AudioSetting.OutSampleRate);
            IAudioResampler audioResampler = this._serviceProvider.GetRequiredService<IAudioResampler>();
            if (!audioResampler.Build(resamplerBuildConfig))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioResampler_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetAudioResampler(audioResampler);
            }
        }
        #endregion

        #region AudioEncoder
        private static void RegisterAudioEncoder(IServiceCollection services)
        {
            services.AddTransient<IAudioEncoder, DefaultOpusEncoder>();
        }

        public void BuildAudioEncoder(Session session)
        {
            IAudioEncoder audioEncoder = this._serviceProvider.GetRequiredService<IAudioEncoder>();
            if (!audioEncoder.Build(session.AudioSetting))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioEncoder_BuildFailed, session.SessionId);
            }
            session.PrivateProvider.SetAudioEncoder(audioEncoder);
        }
        #endregion

        #region VAD
        private static void RegisterVad(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            string modelName = ConvertToKebabCase(config.SelectedSettings["VAD"]);
            switch (modelName)
            {
                case "silero":
                    services.AddKeyedSingleton<IVad, Silero>(key);
                    break;
                case "silero-native":
                    services.AddKeyedTransient<IVad, SileroNative>(modelName);
                    services.AddKeyedTransient<IVad, SileroNative>(key);
                    break;
                default:
                    throw new ModelBuildException("Invalid vad model.");
            }
        }
        #endregion

        #region ASR
        private static void RegisterAsr(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            string modelName = ConvertToKebabCase(config.SelectedSettings["ASR"]);
            switch (modelName)
            {
                case "sense-voice":
                    services.AddKeyedSingleton<IAsr, SenseVoice>(key);
                    break;
                case "paraformer":
                    services.AddKeyedSingleton<IAsr, Paraformer>(key);
                    break;
                default:
                    throw new ModelBuildException("Invalid asr model.");
            }
        }
        #endregion

        #region LLM
        private static void RegisterLlm(IServiceCollection services, XiaoZhiConfig config)
        {
            foreach (var llmSettingItem in config.ConfiguredSettings["LLM"])
            {
                string? endPoint = llmSettingItem.Value.GetConfigValueOrDefault("BaseUrl");
                string? apiKey = llmSettingItem.Value.GetConfigValueOrDefault("ApiKey");
                string? modelId = llmSettingItem.Value.GetConfigValueOrDefault("ModelName");

                if (string.IsNullOrEmpty(endPoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(modelId))
                {
                    throw new ModelBuildException($"Invalid llm model setting, endPoint: {endPoint}, apiKey: {apiKey}, modelId: {modelId}.");
                }

                switch (llmSettingItem.Key.ToLower())
                {
                    case "qwen":
                    case "doubao":
                    case "deepseek":
                    case "chatglm":
                        services.AddOpenAIChatCompletion(modelId, new Uri(endPoint), apiKey, orgId: "Xiao Zhi", $"LLM_{llmSettingItem.Key}");
                        break;
                    default:
                        throw new ModelBuildException("Invalid llm model.");
                }
            }

            services.AddTransient<IFunctionInvocationFilter, MCPToolFunctionFilter>();
            services.AddTransient<IEmotionAgent, EmotionAgent>();
            services.AddTransient<IChatAgent, ChatAgent>();
            services.AddTransient<ILlm, GenericOpenAI>();
        }
        #endregion

        #region LLMPlugins
        private static void RegisterLLMPlugins(IServiceCollection services)
        {
            services.AddTransient<MusicPlayer>();
        }
        #endregion

        #region Memory
        private static void RegisterMemory(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            string modelName = ConvertToKebabCase(config.SelectedSettings["MEMORY"]);
            switch (modelName)
            {
                case "flash-memory":
                    services.AddKeyedTransient<IMemory, FlashMemory>(modelName);
                    services.AddKeyedSingleton<IMemory, FlashMemory>(key);
                    break;
                case "database":
                    services.AddKeyedTransient<IMemory, Database>(modelName);
                    services.AddKeyedSingleton<IMemory, Database>(key);
                    break;
                default:
                    throw new ModelBuildException("Invalid memory model.");
            }
        }
        #endregion

        #region TTS
        private static void RegisterTts(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            string modelName = ConvertToKebabCase(config.SelectedSettings["TTS"]);
            switch (modelName)
            {
                case "kokoro":
                    services.AddKeyedSingleton<ITts, Kokoro>(key);
                    break;
                case "huoshan-bidirection":
                    services.AddKeyedTransient<ITts, HuoshanBidirectionTTS>(modelName);
                    services.AddKeyedTransient<ITts, HuoshanBidirectionTTS>(key);
                    break;
                case "huoshan-unidirectional":
                    services.AddKeyedTransient<ITts, HuoshanUnidirectionalTTS>(modelName);
                    services.AddKeyedTransient<ITts, HuoshanUnidirectionalTTS>(key);
                    break;
                case "huoshan-http":
                    services.AddSingleton(_ => new FlurlClientCache()
                    .Add(nameof(HuoshanHttpTTS), configure: builder =>
                    {
                        builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                    }));
                    services.AddKeyedTransient<ITts, HuoshanHttpTTS>(modelName);
                    services.AddKeyedTransient<ITts, HuoshanHttpTTS>(key);
                    break;
                case "huoshan-http-v3":
                    services.AddSingleton(_ => new FlurlClientCache()
                    .Add(nameof(HuoshanHttpV3TTS), configure: builder =>
                    {
                        builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                    }));
                    services.AddKeyedTransient<ITts, HuoshanHttpV3TTS>(modelName);
                    services.AddKeyedTransient<ITts, HuoshanHttpV3TTS>(key);
                    break;
                default:
                    throw new ModelBuildException("Invalid tts model.");
            }
        }
        #endregion

        #region IoT
        private static void RegisterIoT(IServiceCollection services)
        {
            services.AddTransient<IIoTClient, IoTClient>();
        }
        public void BuildIoT(Session session)
        {
            IIoTClient iotClient = this._serviceProvider.GetRequiredService<IIoTClient>();
            if (!iotClient.Build(session))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildIoT_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetIoTClient(iotClient);
            }
        }
        #endregion

        #region MCP
        private static void RegisterMCP(IServiceCollection services)
        {
            services.AddKeyedTransient<ISubMcpClient, DeviceMcpClient>(SubMCPClientTypeNames.DeviceMcpClient);
            services.AddKeyedTransient<ISubMcpClient, McpEndpointClient>(SubMCPClientTypeNames.McpEndpointClient);
            services.AddKeyedTransient<ISubMcpClient, ServerMcpClient>(SubMCPClientTypeNames.ServerMcpClient);
            services.AddTransient<IMcpClient, McpClient>();
        }
        public void BuildMCP(Session session)
        {
            IMcpClient mcpClient = this._serviceProvider.GetRequiredService<IMcpClient>();

            Dictionary<string, MCPClientBuildConfig> mcpBuildConfigs = new Dictionary<string, MCPClientBuildConfig>();
            if (this._config.McpSettings is null || !this._config.McpSettings.Any())
            {
                mcpBuildConfigs.Add(SubMCPClientTypeNames.DeviceMcpClient, new MCPClientBuildConfig
                (session, new ModelSetting()));
            }
            else
            {
                mcpBuildConfigs = this._config.McpSettings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new MCPClientBuildConfig(session, kvp.Value));
            }
            if (!mcpClient.Build(mcpBuildConfigs))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildMCP_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetMcpClient(mcpClient);
            }
        }
        #endregion

        #region AudioPlayer
        private static void RegisterAudioPlayer(IServiceCollection services)
        {
            services.AddTransient<IMusicPlayer, FileMusicPlayer>();
            services.AddTransient<ISystemNotification, NotificationPlayer>();
            services.AddTransient<IAudioPlayerClient, AudioPlayerClient>();
        }

        public void BuildAudioPlayer(Session session)
        {
            IAudioPlayerClient audioPlayerClient = this._serviceProvider.GetRequiredService<IAudioPlayerClient>();
            if (!audioPlayerClient.Build(session.AudioSetting))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioPlayer_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetAudioPlayerClient(audioPlayerClient);
            }
        }
        #endregion

        #region AudioProcessor
        private static void RegisterAudioMixer(IServiceCollection services)
        {
            services.AddTransient<IAudioProcessor, DefaultAudioProcessor>();
        }

        public void BuildAudioProcessor(Session session)
        {
            IAudioProcessor audioProcessor = this._serviceProvider.GetRequiredService<IAudioProcessor>();
            if (!audioProcessor.Build(session.AudioSetting))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioProcessor_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetAudioProcessor(audioProcessor);
            }
        }
        #endregion
        #endregion

        private bool RegisterGlobalProviders(Session session)
        {
            #region Vad
            IVad genericVad = this._serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD);
            if (!genericVad.IsSherpaModel && !genericVad.Build(this.GetSelectedSetting("VAD", this._config)))
            {
                this._logger.LogError(Lang.ProviderManager_RegisterGlobalProviders_VadBuildFailed, genericVad.ModelName);
                return false;
            }
            session.PrivateProvider.SetVad(genericVad);
            this._logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_VadInitialized, genericVad.ModelName, session.DeviceId);
            #endregion

            #region Asr
            IAsr genericAsr = this._serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR);
            if (!genericAsr.IsSherpaModel && !genericAsr.Build(this.GetSelectedSetting("ASR", this._config)))
            {
                this._logger.LogError(Lang.ProviderManager_RegisterGlobalProviders_AsrBuildFailed, genericAsr.ModelName);
                return false;
            }
            session.PrivateProvider.SetAsr(genericAsr);
            this._logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_AsrInitialized, genericAsr.ModelName, session.DeviceId);
            #endregion

            #region LLM
            Kernel privateKernel = this._globalKernel.Clone();
            ILlm genericLlm = this._serviceProvider.GetRequiredService<ILlm>();

            ModelSetting emotionLLMModelSetting = this.GetSelectedLLMSetting("EmotionLLM", this._config);
            ModelSetting chatLLMModelSetting = this.GetSelectedLLMSetting("ChatLLM", this._config);
            bool useStreaming = chatLLMModelSetting.Config.GetConfigValueOrDefault("UseStreaming", false);
            bool useEmotions = emotionLLMModelSetting.Config.GetConfigValueOrDefault("UseEmotions", false);

            LLMBuildConfig llmBuildConfig = new LLMBuildConfig(
                emotionLLMModelSetting.ModelName,
                chatLLMModelSetting.ModelName,
                this._config.Prompt,
                useStreaming,
                useEmotions,
                SummaryMemory: string.Empty,
                privateKernel);

            privateKernel.Data.Add("session", session);
            if (!genericLlm.Build(llmBuildConfig))
            {
                throw new ModelBuildException("Failed to build generic LLM model.");
            }
            session.PrivateProvider.SetKernel(privateKernel);
            session.PrivateProvider.SetLlm(genericLlm);

            this._logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_LlmInitialized, emotionLLMModelSetting.ModelName, chatLLMModelSetting.ModelName, session.DeviceId);
            #endregion

            #region Tts
            ITts genericTts = this._serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS);
            if (!genericTts.IsSherpaModel && !genericTts.Build(this.GetSelectedSetting("TTS", this._config)))
            {
                this._logger.LogError(Lang.ProviderManager_RegisterGlobalProviders_TtsBuildFailed, genericTts.ModelName);
                return false;
            }
            session.PrivateProvider.SetTts(genericTts);
            this._logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_TtsInitialized, genericTts.ModelName, session.DeviceId);
            #endregion

            return true;
        }

        private static string ConvertToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLower();
        }
    }
}