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
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
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
using XiaoZhi.Net.Server.Providers.LLM.FunctionInvocationFilters;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;
using XiaoZhi.Net.Server.Providers.MCP;
using XiaoZhi.Net.Server.Providers.MCP.DeviceMcp;
using XiaoZhi.Net.Server.Providers.MCP.McpEndpoint;
using XiaoZhi.Net.Server.Providers.MCP.ServerMcp;
using XiaoZhi.Net.Server.Providers.Memory;
using XiaoZhi.Net.Server.Providers.TTS;
using XiaoZhi.Net.Server.Providers.TTS.Sherpa;
using XiaoZhi.Net.Server.Providers.VAD.Sherpa;
using XiaoZhi.Net.Server.Services;

namespace XiaoZhi.Net.Server.Management
{
    internal sealed class ProviderManager
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
                RegisterAudioDecoder(services, GlobalProviderNames.GLOBAL_AUDIO_DECODER);
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

            #region AudioDecoder
            IAudioDecoder audioDecoder = serviceProvider.GetRequiredKeyedService<IAudioDecoder>(GlobalProviderNames.GLOBAL_AUDIO_DECODER);
            if (!audioDecoder.Build(this._config.AudioSetting))
            {
                this._logger.LogError("Failed to build {modelName} provider.", audioDecoder.ModelName);
                return false;
            }
            #endregion

            try
            {
                #region Vad
                IVad vad = serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD);
                if (vad.IsSherpaModel && !vad.Build(this.GetSelectedSetting("VAD", this._config)))
                {
                    this._logger.LogError("Failed to build {modelName} provider.", vad.ModelName);
                    return false;
                }
                #endregion

                #region Asr
                IAsr asr = serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR);
                if (asr.IsSherpaModel && !asr.Build(this.GetSelectedSetting("ASR", this._config)))
                {
                    this._logger.LogError("Failed to build {modelName} provider.", asr.ModelName);
                    return false;
                }
                #endregion

                #region Memory
                IMemory memory = serviceProvider.GetRequiredKeyedService<IMemory>(GlobalProviderNames.GLOBAL_MEMORY);
                if (!memory.Build(this.GetSelectedSetting("MEMORY", this._config)))
                {
                    this._logger.LogError("Failed to build {modelName} provider.", memory.ModelName);
                    return false;
                }
                #endregion

                #region Tts
                ITts tts = serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS);
                if (tts.IsSherpaModel && !tts.Build(this.GetSelectedSetting("TTS", this._config)))
                {
                    this._logger.LogError("Failed to build {modelName} provider.", tts.ModelName);
                    return false;
                }
                #endregion

                #region FFmpeg
                if (MediaFactory.CheckFFmpegInstalled(out string ffmpegVersion))
                {
                    this._logger.LogInformation("FFmpeg is installed successfully, version: {ffmpegVersion}.", ffmpegVersion);
                }
                else
                {
                    this._logger.LogWarning("FFmpeg is not installed or not found, please check your ffmpeg path configuration.");
                    return false;
                }
                #endregion

                return true;
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to build provider components.");
                return false;
            }
        }

        private ModelSetting GetSelectedSetting(string selectedModelType, XiaoZhiConfig config)
        {
            string selectedModel = config.SelectedSettings[selectedModelType];
            dynamic setting = config.ConfiguredSettings[selectedModelType][selectedModel];

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
                    this._logger.LogInformation("Remote service is unavailable or not configured, skipping private models config loading for device: {deviceId}.", session.DeviceId);

                    return this.RegisterGlobalProviders(session);
                }

                PrivateModelsConfig? privateModelsConfig = await manageApiClient.LoadConfigFromApi(session.DeviceId, session.SessionId);

                if (privateModelsConfig is null)
                {
                    this._logger.LogInformation("The device: {deviceId} has not been configured with privatization settings and will use global providers.", session.DeviceId);

                    return this.RegisterGlobalProviders(session);
                }

                IAudioDecoder genericAudioDecoder = this._serviceProvider.GetRequiredKeyedService<IAudioDecoder>(GlobalProviderNames.GLOBAL_AUDIO_DECODER);
                session.PrivateProvider.SetAudioDecoder(genericAudioDecoder);

                if (privateModelsConfig.VadSetting is not null)
                {
                    IVad privateVad = this._serviceProvider.GetRequiredKeyedService<IVad>(privateModelsConfig.VadSetting.ModelName);
                    if (!privateVad.Build(privateModelsConfig.VadSetting))
                    {
                        this._logger.LogError("Failed to build private VAD model for device: {deviceId}.", session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetVad(privateVad);

                    this._logger.LogInformation("Private VAD {modeName} model initialized for device: {deviceId}.", privateModelsConfig.VadSetting.ModelName, session.DeviceId);
                }
                else
                {
                    IVad genericVad = this._serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD);
                    if (!genericVad.IsSherpaModel && !genericVad.Build(this.GetSelectedSetting("VAD", this._config)))
                    {
                        this._logger.LogError("Failed to build {modelName} provider.", genericVad.ModelName);
                        return false;
                    }
                    session.PrivateProvider.SetVad(genericVad);

                    this._logger.LogInformation("Generic VAD {modeName} model initialized for device: {deviceId}.", genericVad.ModelName, session.DeviceId);
                }

                if (privateModelsConfig.AsrSetting is not null)
                {
                    IAsr privateAsr = this._serviceProvider.GetRequiredKeyedService<IAsr>(privateModelsConfig.AsrSetting.ModelName);
                    if (!privateAsr.Build(privateModelsConfig.AsrSetting))
                    {
                        this._logger.LogError("Failed to build private ASR model for device: {deviceId}.", session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetAsr(privateAsr);

                    this._logger.LogInformation("Private ASR {modeName} model initialized for device: {deviceId}.", privateModelsConfig.AsrSetting.ModelName, session.DeviceId);
                }
                else
                {
                    IAsr genericAsr = this._serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR);
                    if (!genericAsr.IsSherpaModel && !genericAsr.Build(this.GetSelectedSetting("ASR", this._config)))
                    {
                        this._logger.LogError("Failed to build {modelName} provider.", genericAsr.ModelName);
                        return false;
                    }
                    session.PrivateProvider.SetAsr(genericAsr);

                    this._logger.LogInformation("Generic ASR {modeName} model initialized for device: {deviceId}.", genericAsr.ModelName, session.DeviceId);
                }

                if (privateModelsConfig.LlmSetting is not null)
                {
                    Kernel privateKernel = this._globalKernel.Clone();
                    ILlm privateLlm = this._serviceProvider.GetRequiredService<ILlm>();

                    string llmModelName = privateModelsConfig.LlmSetting.ModelName;
                    string prompt = privateModelsConfig.LlmSetting.Config.GetConfigValueOrDefault("Prompt", this._config.Prompt);
                    bool useStreaming = privateModelsConfig.LlmSetting.Config.GetConfigValueOrDefault("UseStreaming", false);
                    string summaryMemory = privateModelsConfig.LlmSetting.Config.GetConfigValueOrDefault("SummaryMemory", string.Empty);

                    LLMBuildConfig llmBuildConfig = new LLMBuildConfig(llmModelName, prompt, useStreaming, summaryMemory, privateKernel, session);

                    if (!privateLlm.Build(llmBuildConfig))
                    {
                        this._logger.LogError("Failed to build private LLM model for device: {deviceId}.", session.DeviceId);
                        return false;
                    }
                    privateKernel.Data.Add("session", session);
                    session.PrivateProvider.SetKernel(privateKernel);
                    session.PrivateProvider.SetLlm(privateLlm);

                    this._logger.LogInformation("Private LLM {modeName} model initialized for device: {deviceId}.", privateModelsConfig.LlmSetting.ModelName, session.DeviceId);
                }
                else
                {
                    Kernel privateKernel = this._globalKernel.Clone();
                    ILlm genericLlm = this._serviceProvider.GetRequiredService<ILlm>();

                    ModelSetting llmModelSetting = this.GetSelectedSetting("LLM", this._config);
                    string llmModelName = llmModelSetting.ModelName;
                    bool useStreaming = llmModelSetting.Config.GetConfigValueOrDefault("UseStreaming", false);

                    LLMBuildConfig llmBuildConfig = new LLMBuildConfig(llmModelName, this._config.Prompt, useStreaming, string.Empty, privateKernel, session);

                    if (!genericLlm.Build(llmBuildConfig))
                    {
                        this._logger.LogError("Failed to build generic LLM model for device: {deviceId}.", session.DeviceId);
                        return false;
                    }
                    privateKernel.Data.Add("session", session);
                    session.PrivateProvider.SetKernel(privateKernel);
                    session.PrivateProvider.SetLlm(genericLlm);
                }

                if (privateModelsConfig.TtsSetting is not null)
                {
                    ITts privateTts = this._serviceProvider.GetRequiredKeyedService<ITts>(privateModelsConfig.TtsSetting.ModelName);
                    if (!privateTts.Build(privateModelsConfig.TtsSetting))
                    {
                        this._logger.LogError("Failed to build private TTS model for device: {deviceId}.", session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetTts(privateTts);

                    this._logger.LogInformation("Private TTS {modeName} model initialized for device: {deviceId}.", privateModelsConfig.TtsSetting.ModelName, session.DeviceId);
                }
                else
                {
                    ITts genericTts = this._serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS);
                    if (!genericTts.IsSherpaModel && !genericTts.Build(this.GetSelectedSetting("TTS", this._config)))
                    {
                        this._logger.LogError("Failed to build {modelName} provider.", genericTts.ModelName);
                        return false;
                    }
                    session.PrivateProvider.SetTts(genericTts);

                    this._logger.LogInformation("Generic TTS {modeName} model initialized for device: {deviceId}.", genericTts.ModelName, session.DeviceId);
                }

                this.BuildAudioPlayer(session);
                this.BuildAudioProcessor(session);
                this.BuildAudioResampler(session);
                this.BuildAudioEncoder(session);

                return true;
            }
            catch (DeviceNotFoundException)
            {
                session.IsDeviceBinded = false;
                this._logger.LogWarning("The device: {deviceId} is not found, it may not be registered.", session.DeviceId);
                return true;
            }
            catch (DeviceBindException deviceBindException)
            {
                session.IsDeviceBinded = false;
                session.BindCode = deviceBindException.BindCode;
                this._logger.LogWarning("The device: {deviceId} is not binded, bind code: {bindCode}.", session.DeviceId, session.BindCode);
                return true;
            }
            catch (Exception ex)
            {
                session.IsDeviceBinded = false;
                this._logger.LogError(ex, "Failed to load private models config for device: {deviceId} with session: {sessionId}.", session.DeviceId, session.SessionId);
                return false;
            }
        }

        public async Task SaveMemoryAsync(Session session)
        {
            if (session.PrivateProvider.Llm is null)
            {
                this._logger.LogWarning("LLM provider is not initialized, cannot save memory for device: {deviceId}.", session.DeviceId);
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
                        this._logger.LogInformation("Memory saved successfully for device: {deviceId} with session: {sessionId}.", session.DeviceId, session.SessionId);
                    }
                    catch (Exception ex)
                    {
                        this._logger.LogError(ex, "Failed to save memory for device: {deviceId} with session: {sessionId}.", session.DeviceId, session.SessionId);
                    }
                }
                else
                {
                    this._logger.LogWarning("ManageApiClient is not available, cannot save memory for device: {deviceId} with session: {sessionId}.", session.DeviceId, session.SessionId);
                }
            }
        }

        public void Dispose(IServiceProvider serviceProvider)
        {
            IList<IDisposable> providers = new List<IDisposable>
            {
                serviceProvider.GetRequiredKeyedService<IAudioDecoder>(GlobalProviderNames.GLOBAL_AUDIO_DECODER),
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
        private static void RegisterAudioDecoder(IServiceCollection services, string key)
        {
            services.AddTransient<IAudioDecoder, DefaultOpusDecoder>();
            services.AddKeyedSingleton<IAudioDecoder, DefaultOpusDecoder>(key);
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

            if (ttsSampleRate == session.AudioSetting.SampleRate)
            {
                return;
            }

            this._logger.LogInformation("Device {deviceId} requires audio resampling from {ttsSampleRate} to {deviceSampleRate}.", session.DeviceId, ttsSampleRate, session.AudioSetting.SampleRate);

            ResamplerBuildConfig resamplerBuildConfig = new ResamplerBuildConfig(session.AudioSetting.Channels, ttsSampleRate, session.AudioSetting.SampleRate);
            IAudioResampler audioResampler = this._serviceProvider.GetRequiredService<IAudioResampler>();
            if (!audioResampler.Build(resamplerBuildConfig))
            {
                this._logger.LogWarning("Session {sessionId} failed to build audio resampler.", session.SessionId);
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
                this._logger.LogWarning("Session {sessionId} failed to build audio encoder.", session.SessionId);
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
                    services.AddKeyedTransient<IVad, Silero>(modelName);
                    services.AddKeyedSingleton<IVad, Silero>(key);
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
                    services.AddKeyedTransient<IAsr, SenseVoice>(modelName);
                    services.AddKeyedSingleton<IAsr, SenseVoice>(key);
                    break;
                case "paraformer":
                    services.AddKeyedTransient<IAsr, Paraformer>(modelName);
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

                switch (ConvertToKebabCase(llmSettingItem.Key))
                {
                    case "qwen":
                    case "doubao":
                    case "deepseek":
                    case "chat-glm":
                        services.AddOpenAIChatCompletion(modelId, new Uri(endPoint), apiKey, orgId: "Xiao Zhi", $"LLM_{llmSettingItem.Key}");
                        break;
                    default:
                        throw new ModelBuildException("Invalid llm model.");
                }
            }

            services.AddTransient<IFunctionInvocationFilter, MCPToolFunctionFilter>();
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
                this._logger.LogWarning("Session {sessionId} failed to build IoT client.", session.SessionId);
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
                this._logger.LogWarning("Session {sessionId} failed to build MCP client.", session.SessionId);
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
                this._logger.LogWarning("Session {sessionId} failed to build audio player.", session.SessionId);
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
                this._logger.LogWarning("Session {sessionId} failed to build audio mixer.", session.SessionId);
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
            #region AudioDecoder
            IAudioDecoder genericAudioDecoder = this._serviceProvider.GetRequiredKeyedService<IAudioDecoder>(GlobalProviderNames.GLOBAL_AUDIO_DECODER);
            session.PrivateProvider.SetAudioDecoder(genericAudioDecoder);
            this._logger.LogInformation("Generic AudioDecoder {modeName} model initialized for device: {deviceId}.", genericAudioDecoder.ModelName, session.DeviceId);
            #endregion

            #region Vad
            IVad genericVad = this._serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD);
            if (!genericVad.IsSherpaModel && !genericVad.Build(this.GetSelectedSetting("VAD", this._config)))
            {
                this._logger.LogError("Failed to build {modelName} provider.", genericVad.ModelName);
                return false;
            }
            session.PrivateProvider.SetVad(genericVad);
            this._logger.LogInformation("Generic VAD {modeName} model initialized for device: {deviceId}.", genericVad.ModelName, session.DeviceId);
            #endregion

            #region Asr
            IAsr genericAsr = this._serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR);
            if (!genericAsr.IsSherpaModel && !genericAsr.Build(this.GetSelectedSetting("ASR", this._config)))
            {
                this._logger.LogError("Failed to build {modelName} provider.", genericAsr.ModelName);
                return false;
            }
            session.PrivateProvider.SetAsr(genericAsr);
            this._logger.LogInformation("Generic ASR {modeName} model initialized for device: {deviceId}.", genericAsr.ModelName, session.DeviceId);
            #endregion

            #region LLM
            Kernel privateKernel = this._globalKernel.Clone();
            ILlm genericLlm = this._serviceProvider.GetRequiredService<ILlm>();

            ModelSetting llmModelSetting = this.GetSelectedSetting("LLM", this._config);
            string llmModelName = llmModelSetting.ModelName;
            bool useStreaming = llmModelSetting.Config.GetConfigValueOrDefault("UseStreaming", false);

            LLMBuildConfig llmBuildConfig = new LLMBuildConfig(llmModelName, this._config.Prompt, useStreaming, string.Empty, privateKernel, session);

            if (!genericLlm.Build(llmBuildConfig))
            {
                throw new ModelBuildException("Failed to build generic LLM model.");
            }
            privateKernel.Data.Add("session", session);
            session.PrivateProvider.SetKernel(privateKernel);
            session.PrivateProvider.SetLlm(genericLlm);

            this._logger.LogInformation("Generic LLM {modeName} model initialized for device: {deviceId}.", llmModelSetting.ModelName, session.DeviceId);
            #endregion

            #region Tts
            ITts genericTts = this._serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS);
            if (!genericTts.IsSherpaModel && !genericTts.Build(this.GetSelectedSetting("TTS", this._config)))
            {
                this._logger.LogError("Failed to build {modelName} provider.", genericTts.ModelName);
                return false;
            }
            session.PrivateProvider.SetTts(genericTts);
            this._logger.LogInformation("Generic TTS {modeName} model initialized for device: {deviceId}.", genericTts.ModelName, session.DeviceId);
            #endregion

            this.BuildAudioPlayer(session);
            this.BuildAudioProcessor(session);
            this.BuildAudioResampler(session);
            this.BuildAudioEncoder(session);

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