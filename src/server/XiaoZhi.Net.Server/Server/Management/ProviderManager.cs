using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Flurl.Http.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using XiaoZhi.Net.Server.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.ASR.Aliyun;
using XiaoZhi.Net.Server.Providers.ASR.Huoshan;
using XiaoZhi.Net.Server.Providers.ASR.Sherpa;
using XiaoZhi.Net.Server.Providers.AudioCodec;
using XiaoZhi.Net.Server.Providers.AudioMixer;
using XiaoZhi.Net.Server.Providers.AudioPlayer;
using XiaoZhi.Net.Server.Providers.AudioPlayer.Music;
using XiaoZhi.Net.Server.Providers.AudioPlayer.SystemNotification;
using XiaoZhi.Net.Server.Providers.IoT;
using XiaoZhi.Net.Server.Providers.LLM;
using XiaoZhi.Net.Server.Providers.LLM.Agents;
using XiaoZhi.Net.Server.Providers.LLM.Agents.Intent;
using XiaoZhi.Net.Server.Providers.LLM.Utils;
using XiaoZhi.Net.Server.Providers.MCP;
using XiaoZhi.Net.Server.Providers.MCP.DeviceMcp;
using XiaoZhi.Net.Server.Providers.MCP.McpEndpoint;
using XiaoZhi.Net.Server.Providers.MCP.ServerMcp;
using XiaoZhi.Net.Server.Providers.Memory;
using XiaoZhi.Net.Server.Providers.TTS;
using XiaoZhi.Net.Server.Providers.TTS.Aliyun;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan;
using XiaoZhi.Net.Server.Providers.TTS.Sherpa;
using XiaoZhi.Net.Server.Providers.VAD.Native;
using XiaoZhi.Net.Server.Providers.VAD.Sherpa;
using XiaoZhi.Net.Server.Services;

namespace XiaoZhi.Net.Server.Management
{
    internal class ProviderManager : BaseManager
    {

        public ProviderManager(IServiceProvider serviceProvider, XiaoZhiConfig config, ILogger<ProviderManager> logger) : base(serviceProvider, config, logger)
        {
        }

        public static IHostBuilder RegisterServices(IHostBuilder builder, XiaoZhiConfig config)
        {
            return builder.ConfigureServices((context, services) =>
            {
                RegisterAudioDecoder(services);
                RegisterVad(services, config);
                RegisterAsr(services, config);
                RegisterLlm(services, config);
                RegisterMemory(services, config);
                RegisterTts(services, config);

                RegisterAudioEncoder(services);
                RegisterAudioResampler(services);
                RegisterAudioMixer(services);
                RegisterIoT(services);
                RegisterMCP(services);
                RegisterAudioPlayer(services);

                services.AddSingleton<ProviderManager>();
            });
        }

        public override bool BuildComponent()
        {
            try
            {
                #region Vad
                string selectedVadModelName = ConvertToKebabCase(this.Config.SelectedSettings["VAD"]);
                IVad vad = this.ServiceProvider.GetRequiredKeyedService<IVad>(selectedVadModelName);
                if (SherpaModels.VadModels.Contains(selectedVadModelName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!vad.Build(this.GetSelectedSetting("VAD", this.Config)))
                    {
                        this.Logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, vad.ModelName);
                        return false;
                    }
                }
                #endregion

                #region Asr
                string selectedAsrModelName = ConvertToKebabCase(this.Config.SelectedSettings["ASR"]);
                if (SherpaModels.AsrModels.Contains(selectedAsrModelName, StringComparer.OrdinalIgnoreCase))
                {
                    IAsr asr = this.ServiceProvider.GetRequiredKeyedService<IAsr>(selectedAsrModelName);
                    if (!asr.Build(this.GetSelectedSetting("ASR", this.Config)))
                    {
                        this.Logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, asr.ModelName);
                        return false;
                    }
                }
                #endregion

                #region Memory
                string selectedMemoryModelName = ConvertToKebabCase(this.Config.SelectedSettings["Memory"]);
                IMemory memory = this.ServiceProvider.GetRequiredKeyedService<IMemory>(selectedMemoryModelName);
                if (!memory.Build(this.GetSelectedSetting("Memory", this.Config)))
                {
                    this.Logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, memory.ModelName);
                    return false;
                }
                #endregion

                #region Tts
                string selectedTtsModelName = ConvertToKebabCase(this.Config.SelectedSettings["TTS"]);
                if (SherpaModels.TtsModels.Contains(selectedTtsModelName, StringComparer.OrdinalIgnoreCase))
                {
                    ITts tts = this.ServiceProvider.GetRequiredKeyedService<ITts>(selectedTtsModelName);
                    if (!tts.Build(this.GetSelectedSetting("TTS", this.Config)))
                    {
                        this.Logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, tts.ModelName);
                        return false;
                    }
                }
                #endregion

                #region FFmpeg
                if (MediaFactory.CheckFFmpegInstalled(out string ffmpegVersion))
                {
                    this.Logger.LogInformation(Lang.ProviderManager_BuildComponent_FFmpegInstalled, ffmpegVersion);
                }
                else
                {
                    this.Logger.LogWarning(Lang.ProviderManager_BuildComponent_FFmpegNotFound);
                    return false;
                }
                #endregion

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.ProviderManager_BuildComponent_BuildComponentsFailed);
                return false;
            }
        }

        public override async Task OnSessionClosedAsync(Session session)
        {
            if (session.PrivateProvider.Llm is null)
            {
                this.Logger.LogWarning(Lang.ProviderManager_SaveMemory_LlmNotInitialized, session.DeviceId);
                return;
            }

            IReadOnlyList<ChatMessage> chatHistory = session.PrivateProvider.Llm.GetChatHistory();
            if (chatHistory.Any())
            {
                ManageApiClient? manageApiClient = this.ServiceProvider.GetService<ManageApiClient>();
                if (manageApiClient is not null)
                {
                    try
                    {
                        await manageApiClient.SaveMemoryAsync(session.DeviceId, session.SessionId, chatHistory);
                        this.Logger.LogInformation(Lang.ProviderManager_SaveMemory_MemorySaved, session.DeviceId, session.SessionId);
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError(ex, Lang.ProviderManager_SaveMemory_SaveMemoryFailed, session.DeviceId, session.SessionId);
                    }
                }
                else
                {
                    this.Logger.LogWarning(Lang.ProviderManager_SaveMemory_ApiClientNotAvailable, session.DeviceId, session.SessionId);
                }
            }
        }

        public override async Task<bool> OnSessionPropertyInitializingAsync(Session session)
        {
            try
            {
                ManageApiClient? manageApiClient = this.ServiceProvider.GetService<ManageApiClient>();

                if (manageApiClient is null)
                {
                    this.Logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_RemoteServiceUnavailable, session.DeviceId);

                    session.IsDeviceBinded = true; // Assume device is binded if manage API is not available

                    return this.RegisterGlobalProviders(session);
                }

                PrivateModelsConfig? privateModelsConfig = await manageApiClient.LoadConfigFromApi(session.DeviceId, session.SessionId);

                if (privateModelsConfig is null)
                {
                    this.Logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_NoPrivateConfig, session.DeviceId);

                    return this.RegisterGlobalProviders(session);
                }

                if (privateModelsConfig.VadSetting is not null)
                {
                    IVad privateVad = this.ServiceProvider.GetRequiredKeyedService<IVad>(privateModelsConfig.VadSetting.ModelName);
                    if (!privateVad.Build(privateModelsConfig.VadSetting))
                    {
                        this.Logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateVadBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetVad(privateVad);

                    this.Logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateVadInitialized, privateModelsConfig.VadSetting.ModelName, session.DeviceId);
                }
                else
                {
                    bool vadRegistered = this.RegisterGlobalVadProviders(session);
                    if (!vadRegistered)
                    {
                        return false;
                    }
                }

                if (privateModelsConfig.AsrSetting is not null)
                {
                    IAsr privateAsr = this.ServiceProvider.GetRequiredKeyedService<IAsr>(privateModelsConfig.AsrSetting.ModelName);
                    if (!privateAsr.Build(privateModelsConfig.AsrSetting))
                    {
                        this.Logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateAsrBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetAsr(privateAsr);

                    this.Logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateAsrInitialized, privateModelsConfig.AsrSetting.ModelName, session.DeviceId);
                }
                else
                {
                    bool asrRegistered = this.RegisterGlobalAsrProviders(session);
                    if (!asrRegistered)
                    {
                        return false;
                    }
                }

                if (privateModelsConfig.AgentSettings.Any())
                {
                    ILlm privateLlm = this.ServiceProvider.GetRequiredService<ILlm>();

                    LLMBuildConfig llmBuildConfig = new LLMBuildConfig(
                        privateModelsConfig.AgentSettings,
                        session.PrivateProvider);

                    if (!privateLlm.Build(llmBuildConfig))
                    {
                        this.Logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateLlmBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetLlm(privateLlm);

                    this.Logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateLlmInitialized, session.DeviceId);
                }
                else
                {
                    bool llmRegistered = this.RegisterGlobalLlmProviders(session);
                    if (!llmRegistered)
                    {
                        return false;
                    }
                }

                if (privateModelsConfig.TtsSetting is not null)
                {
                    ITts privateTts = this.ServiceProvider.GetRequiredKeyedService<ITts>(privateModelsConfig.TtsSetting.ModelName);
                    if (!privateTts.Build(privateModelsConfig.TtsSetting))
                    {
                        this.Logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateTtsBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetTts(privateTts);

                    this.Logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateTtsInitialized, privateModelsConfig.TtsSetting.ModelName, session.DeviceId);
                }
                else
                {
                    bool ttsRegistered = this.RegisterGlobalTtsProviders(session);
                    if (!ttsRegistered)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (DeviceNotFoundException)
            {
                session.IsDeviceBinded = false;
                this.Logger.LogWarning(Lang.ProviderManager_InitializePrivateConfig_DeviceNotFound, session.DeviceId);
                return true;
            }
            catch (DeviceBindException deviceBindException)
            {
                session.IsDeviceBinded = false;
                session.BindCode = deviceBindException.BindCode;
                this.Logger.LogWarning(Lang.ProviderManager_InitializePrivateConfig_DeviceNotBinded, session.DeviceId, session.BindCode);
                return true;
            }
            catch (Exception ex)
            {
                session.IsDeviceBinded = false;
                this.Logger.LogError(ex, Lang.ProviderManager_InitializePrivateConfig_LoadPrivateConfigFailed, session.DeviceId, session.SessionId);
                return false;
            }
            finally
            {
                this.BuildAudioDecoder(session);
                this.BuildInputAudioResampler(session);
                this.BuildAudioPlayer(session);
                this.BuildAudioProcessor(session);
                this.BuildOutputAudioResampler(session);
                this.BuildAudioEncoder(session);
            }
        }


        public override Task OnSessionPropertyInitializedAsync(Session session, JsonObject helloMessage)
        {
            if (helloMessage.TryGetPropertyValue("features", out var features) && features is not null)
            {
                JsonObject featuresObj = features.AsObject();
                if (featuresObj.TryGetPropertyValue("mcp", out var mcp) && mcp is not null)
                {
                    bool isSupportMCP = mcp.GetValue<bool>();
                    if (isSupportMCP)
                    {
                        this.BuildMCP(session);
                    }
                }
            }
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            string selectedVadModelName = ConvertToKebabCase(this.Config.SelectedSettings["VAD"]);
            string selectedAsrModelName = ConvertToKebabCase(this.Config.SelectedSettings["ASR"]);
            string selectedTtsModelName = ConvertToKebabCase(this.Config.SelectedSettings["TTS"]);
            IList<IDisposable> providers = new List<IDisposable>
            {
                this.ServiceProvider.GetRequiredKeyedService<IAsr>(selectedVadModelName),
                this.ServiceProvider.GetRequiredKeyedService<IVad>(selectedAsrModelName),
                this.ServiceProvider.GetRequiredKeyedService<IMemory>(selectedTtsModelName)
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
            IAudioDecoder audioDecoder = this.ServiceProvider.GetRequiredService<IAudioDecoder>();
            if (!audioDecoder.Build(session.AudioSetting))
            {
                this.Logger.LogWarning(Lang.ProviderManager_BuildAudioDecoder_BuildFailed, session.SessionId);
            }
            session.PrivateProvider.SetAudioDecoder(audioDecoder);
        }
        #endregion

        #region AudioResampler
        private static void RegisterAudioResampler(IServiceCollection services)
        {
            services.AddTransient<IAudioResampler, DefaultResampler>();
        }
        public void BuildOutputAudioResampler(Session session)
        {
            int audioProcessorSampleRate = this.Config.AudioSetting.SampleRate;

            if (audioProcessorSampleRate == session.AudioSetting.SampleRate)
            {
                return;
            }

            this.Logger.LogInformation(Lang.ProviderManager_BuildOutputAudioResampler_ResamplingRequired, session.DeviceId, audioProcessorSampleRate, session.AudioSetting.SampleRate);

            ResamplerBuildConfig resamplerBuildConfig = new ResamplerBuildConfig(session.AudioSetting.Channels, audioProcessorSampleRate, session.AudioSetting.SampleRate);
            IAudioResampler audioResampler = this.ServiceProvider.GetRequiredService<IAudioResampler>();
            if (!audioResampler.Build(resamplerBuildConfig))
            {
                this.Logger.LogWarning(Lang.ProviderManager_BuildOutputAudioResampler_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetOutputAudioResampler(audioResampler);
            }
        }

        public void BuildInputAudioResampler(Session session)
        {
            if (session.AudioSetting.SampleRate == GlobalVariables.AudioProcessingSampleRate
                && session.AudioSetting.Channels == GlobalVariables.AudioProcessingChannels)
            {
                return;
            }

            ResamplerBuildConfig resamplerBuildConfig = new(
                GlobalVariables.AudioProcessingChannels,
                session.AudioSetting.SampleRate,
                GlobalVariables.AudioProcessingSampleRate);
            IAudioResampler inputAudioResampler = this.ServiceProvider.GetRequiredService<IAudioResampler>();
            if (!inputAudioResampler.Build(resamplerBuildConfig))
            {
                this.Logger.LogWarning(Lang.ProviderManager_BuildInputAudioResampler_BuildFailed, session.SessionId);
                return;
            }

            session.PrivateProvider.SetInputAudioResampler(inputAudioResampler);
            this.Logger.LogInformation(
                Lang.ProviderManager_BuildInputAudioResampler_ResamplingRequired,
                session.DeviceId, session.AudioSetting.SampleRate, GlobalVariables.AudioProcessingSampleRate);
        }
        #endregion

        #region AudioEncoder
        private static void RegisterAudioEncoder(IServiceCollection services)
        {
            services.AddTransient<IAudioEncoder, DefaultOpusEncoder>();
        }

        public void BuildAudioEncoder(Session session)
        {
            IAudioEncoder audioEncoder = this.ServiceProvider.GetRequiredService<IAudioEncoder>();
            if (!audioEncoder.Build(session.AudioSetting))
            {
                this.Logger.LogWarning(Lang.ProviderManager_BuildAudioEncoder_BuildFailed, session.SessionId);
            }
            session.PrivateProvider.SetAudioEncoder(audioEncoder);
        }
        #endregion

        #region VAD
        private static void RegisterVad(IServiceCollection services, XiaoZhiConfig config)
        {
            foreach (var vadSettingItem in config.ConfiguredSettings["VAD"])
            {
                string modelName = ConvertToKebabCase(vadSettingItem.Key);
                switch (modelName)
                {
                    case "silero":
                        services.AddKeyedSingleton<IVad, Silero>(modelName);
                        break;
                    case "silero-native":
                        services.AddKeyedTransient<IVad, SileroNative>(modelName);
                        break;
                    default:
                        throw new ModelBuildException("Invalid vad model.");
                }
            }
        }
        #endregion

        #region ASR
        private static void RegisterAsr(IServiceCollection services, XiaoZhiConfig config)
        {
            foreach (var asrSettingItem in config.ConfiguredSettings["ASR"])
            {
                string modelName = ConvertToKebabCase(asrSettingItem.Key);
                switch (modelName)
                {
                    case "sense-voice":
                        services.AddKeyedSingleton<IAsr, SenseVoice>(modelName);
                        break;
                    case "paraformer":
                        services.AddKeyedSingleton<IAsr, Paraformer>(modelName);
                        break;
                    case "huoshan-unidirectional":
                        services.AddKeyedTransient<IAsr, HuoshanUnidirectionalASR>(modelName);
                        break;
                    case "huoshan-bidirection":
                        services.AddKeyedTransient<IAsr, HuoshanBidirectionASR>(modelName);
                        break;
                    case "aliyun-realtime":
                        services.AddKeyedTransient<IAsr, AliyunRealtimeASR>(modelName);
                        break;
                    default:
                        throw new ModelBuildException("Invalid asr model.");
                }
            }
        }
        #endregion

        #region LLM
        private static void RegisterLlm(IServiceCollection services, XiaoZhiConfig config)
        {
            foreach (var llmSettingItem in config.ConfiguredSettings["LLM"])
            {
                string? type = llmSettingItem.Value.GetConfigValueOrDefault("Type");
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }
                if (!type.Equals(GlobalVariables.ChatAgentType, StringComparison.OrdinalIgnoreCase))
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

                string llmProviderKey = $"LLM_{llmSettingItem.Key}";

                // 注册 IChatClient，使用 MEAI OpenAI 适配器
                services.AddKeyedSingleton<IChatClient>(llmProviderKey, (_, _) =>
                {
                    OpenAIClient openAIClient = new OpenAIClient(
                        new ApiKeyCredential(apiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(endPoint) });
                    return openAIClient.GetChatClient(modelId).AsIChatClient();
                });
            }

            services.AddKeyedTransient<IAgent, InputAgent>(SubAgentNames.InputAgent);
            services.AddKeyedTransient<IAgent, IntentDetectionAgent>(SubAgentNames.IntentDetectionAgent);
            services.AddKeyedTransient<IAgent, FunctionCallAgent>(SubAgentNames.FunctionCallAgent);
            services.AddKeyedTransient<IAgent, IntentResponseAgent>(SubAgentNames.IntentResponseAgent);
            services.AddKeyedTransient<IAgent, ChatAgent>(SubAgentNames.ChatAgent);
            services.AddKeyedTransient<IAgent, OutputAgent>(SubAgentNames.OutputAgent);
            services.AddTransient<ILlm, GenericOpenAI>();
            services.AddTransient<ChatHistorySequence>();
        }
        #endregion

        #region Memory
        private static void RegisterMemory(IServiceCollection services, XiaoZhiConfig config)
        {
            foreach (var memorySettingItem in config.ConfiguredSettings["Memory"])
            {
                string modelName = ConvertToKebabCase(memorySettingItem.Key);
                switch (modelName)
                {
                    case "flash-memory":
                        services.AddKeyedTransient<IMemory, FlashMemory>(modelName);
                        break;
                    case "database":
                        services.AddKeyedTransient<IMemory, Database>(modelName);
                        break;
                    default:
                        throw new ModelBuildException("Invalid memory model.");
                }
            }
        }
        #endregion

        #region TTS
        private static void RegisterTts(IServiceCollection services, XiaoZhiConfig config)
        {
            foreach (var ttsSettingItem in config.ConfiguredSettings["TTS"])
            {
                string modelName = ConvertToKebabCase(ttsSettingItem.Key);
                switch (modelName)
                {
                    case "kokoro":
                        services.AddKeyedSingleton<ITts, Kokoro>(modelName);
                        break;
                    case "huoshan-bidirection":
                        services.AddKeyedTransient<ITts, HuoshanBidirectionTTS>(modelName);
                        break;
                    case "aliyun-realtime-t-t-s":
                    case "aliyun-realtime-tts":
                        services.AddKeyedTransient<ITts, AliyunRealtimeTTS>(modelName);
                        break;
                    /*
                    case "huoshan-unidirectional":
                        services.AddKeyedTransient<ITts, HuoshanUnidirectionalTTS>(modelName);
                        break;
                    */
                    case "huoshan-http":
                        services.AddKeyedSingleton<IFlurlClientCache>(nameof(HuoshanHttpTTS), (_, _) => new FlurlClientCache()
                        .Add(nameof(HuoshanHttpTTS), configure: builder =>
                        {
                            builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                        }));
                        services.AddKeyedTransient<ITts, HuoshanHttpTTS>(modelName);
                        break;
                    case "huoshan-http-v3":
                        services.AddKeyedSingleton<IFlurlClientCache>(nameof(HuoshanHttpV3TTS), (_, _) => new FlurlClientCache()
                        .Add(nameof(HuoshanHttpV3TTS), configure: builder =>
                        {
                            builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                        }));
                        services.AddKeyedTransient<ITts, HuoshanHttpV3TTS>(modelName);
                        break;
                    case "aliyun-http":
                        services.AddKeyedSingleton<IFlurlClientCache>(nameof(AliyunHttpTTS), (_, _) => new FlurlClientCache()
                        .Add(nameof(AliyunHttpTTS), configure: builder =>
                        {
                            builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                        }));
                        services.AddKeyedTransient<ITts, AliyunHttpTTS>(modelName);
                        break;
                    default:
                        throw new ModelBuildException("Invalid tts model.");
                }
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
            IIoTClient iotClient = this.ServiceProvider.GetRequiredService<IIoTClient>();
            if (!iotClient.Build(session))
            {
                this.Logger.LogWarning(Lang.ProviderManager_BuildIoT_BuildFailed, session.SessionId);
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
            // 在发起 MCP 握手前先标记"等待工具列表加载"，防止对话提前使用空工具列表
            session.PrivateProvider.FunctionToolsContext.SetMcpClientPending();

            IMcpClient mcpClient = this.ServiceProvider.GetRequiredService<IMcpClient>();

            Dictionary<string, MCPClientBuildConfig> mcpBuildConfigs = new Dictionary<string, MCPClientBuildConfig>();
            if (this.Config.McpSettings is null || !this.Config.McpSettings.Any())
            {
                mcpBuildConfigs.Add(SubMCPClientTypeNames.DeviceMcpClient, new MCPClientBuildConfig
                (session, new ModelSetting()));
            }
            else
            {
                mcpBuildConfigs = this.Config.McpSettings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new MCPClientBuildConfig(session, kvp.Value));
            }
            if (!mcpClient.Build(mcpBuildConfigs))
            {
                session.PrivateProvider.FunctionToolsContext.SetMcpClientFailed(new InvalidOperationException($"MCP client build failed for session '{session.SessionId}'."));
                this.Logger.LogWarning(Lang.ProviderManager_BuildMCP_BuildFailed, session.SessionId);
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
            IAudioPlayerClient audioPlayerClient = this.ServiceProvider.GetRequiredService<IAudioPlayerClient>();
            if (!audioPlayerClient.Build(this.Config.AudioSetting))
            {
                this.Logger.LogWarning(Lang.ProviderManager_BuildAudioPlayer_BuildFailed, session.SessionId);
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
            IAudioProcessor audioProcessor = this.ServiceProvider.GetRequiredService<IAudioProcessor>();
            if (!audioProcessor.Build(this.Config.AudioSetting))
            {
                this.Logger.LogWarning(Lang.ProviderManager_BuildAudioProcessor_BuildFailed, session.SessionId);
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
            bool vadRegistered = this.RegisterGlobalVadProviders(session);
            bool asrRegistered = this.RegisterGlobalAsrProviders(session);
            bool llmRegistered = this.RegisterGlobalLlmProviders(session);
            bool ttsRegistered = this.RegisterGlobalTtsProviders(session);

            return vadRegistered && asrRegistered && llmRegistered && ttsRegistered;
        }

        private bool RegisterGlobalVadProviders(Session session)
        {
            string selectedVadModelName = ConvertToKebabCase(this.Config.SelectedSettings["VAD"]);
            IVad genericVad = this.ServiceProvider.GetRequiredKeyedService<IVad>(selectedVadModelName);
            if (!genericVad.IsSherpaModel && !genericVad.Build(this.GetSelectedSetting("VAD", this.Config)))
            {
                this.Logger.LogError(Lang.ProviderManager_RegisterGlobalProviders_VadBuildFailed, genericVad.ModelName);
                return false;
            }
            session.PrivateProvider.SetVad(genericVad);
            this.Logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_VadInitialized, session.DeviceId, genericVad.ModelName);
            return true;
        }

        private bool RegisterGlobalAsrProviders(Session session)
        {
            string selectedAsrModelName = ConvertToKebabCase(this.Config.SelectedSettings["ASR"]);
            IAsr genericAsr = this.ServiceProvider.GetRequiredKeyedService<IAsr>(selectedAsrModelName);
            if (!genericAsr.IsSherpaModel && !genericAsr.Build(this.GetSelectedSetting("ASR", this.Config)))
            {
                this.Logger.LogError(Lang.ProviderManager_RegisterGlobalProviders_AsrBuildFailed, genericAsr.ModelName);
                return false;
            }
            session.PrivateProvider.SetAsr(genericAsr);
            this.Logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_AsrInitialized, session.DeviceId, genericAsr.ModelName);
            return true;
        }

        private bool RegisterGlobalLlmProviders(Session session)
        {
            ILlm genericLlm = this.ServiceProvider.GetRequiredService<ILlm>();

            ModelSetting selectedIntentLLMModelSetting = this.GetSelectedSetting("Intent", this.Config);
            string intentType = selectedIntentLLMModelSetting.Config.GetConfigValueOrDefault("Type", "None");

            ModelSetting selectedChatLLMModelSetting = this.GetSelectedSetting("LLM", this.Config);
            selectedChatLLMModelSetting.Config.SetConfigValue("Prompt", this.Config.Prompt);
            selectedChatLLMModelSetting.Config.SetConfigValue("IntentType", intentType);

            ModelSetting intentResponseAgentSetting = new ModelSetting
            {
                ModelName = selectedIntentLLMModelSetting.ModelName,
                Config = new Dictionary<string, string>(selectedIntentLLMModelSetting.Config)
            };

            ModelSetting inputAgentSetting = new ModelSetting
            {
                ModelName = SubAgentNames.InputAgent,
                Config = new Dictionary<string, string>
                {
                   { "IntentType", intentType }
                }
            };

            ModelSetting outputAgentSetting = new ModelSetting
            {
                ModelName = SubAgentNames.OutputAgent,
                Config = new Dictionary<string, string>(0)
            };

            Dictionary<string, ModelSetting> agentSettings = new Dictionary<string, ModelSetting>
            {
                { SubAgentNames.InputAgent, inputAgentSetting },
                { SubAgentNames.IntentDetectionAgent, selectedIntentLLMModelSetting },
                { SubAgentNames.FunctionCallAgent, new ModelSetting { ModelName = SubAgentNames.FunctionCallAgent, Config = new Dictionary<string, string>(0) } },
                { SubAgentNames.IntentResponseAgent, intentResponseAgentSetting },
                { SubAgentNames.ChatAgent, selectedChatLLMModelSetting },
                { SubAgentNames.OutputAgent, outputAgentSetting },
            };

            LLMBuildConfig llmBuildConfig = new LLMBuildConfig(
                agentSettings,
                session.PrivateProvider);

            if (!genericLlm.Build(llmBuildConfig))
            {
                this.Logger.LogError(Lang.ProviderManager_InitializePrivateConfig_GenericLlmBuildFailed, session.DeviceId);
                return false;
            }
            session.PrivateProvider.SetLlm(genericLlm);

            this.Logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_GenericLlmInitialized, session.DeviceId);
            return true;
        }

        private bool RegisterGlobalTtsProviders(Session session)
        {
            string selectedTtsModelName = ConvertToKebabCase(this.Config.SelectedSettings["TTS"]);
            ITts genericTts = this.ServiceProvider.GetRequiredKeyedService<ITts>(selectedTtsModelName);
            if (!genericTts.IsSherpaModel && !genericTts.Build(this.GetSelectedSetting("TTS", this.Config)))
            {
                this.Logger.LogError(Lang.ProviderManager_RegisterGlobalProviders_TtsBuildFailed, genericTts.ModelName);
                return false;
            }
            session.PrivateProvider.SetTts(genericTts);
            this.Logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_TtsInitialized, session.DeviceId, genericTts.ModelName);
            return true;
        }
    }
}
