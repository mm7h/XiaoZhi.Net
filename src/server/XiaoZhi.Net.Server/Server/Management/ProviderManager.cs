using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.ASR;
using XiaoZhi.Net.Server.Providers.AudioCodec;
using XiaoZhi.Net.Server.Providers.LLM;
using XiaoZhi.Net.Server.Providers.Memory;
using XiaoZhi.Net.Server.Providers.Punctuation;
using XiaoZhi.Net.Server.Providers.TTS;
using XiaoZhi.Net.Server.Providers.VAD;
using XiaoZhi.Net.Server.Providers.MCP;
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

        public static void RegisterServices(HostApplicationBuilder builder, XiaoZhiConfig config)
        {
            IServiceCollection services = builder.Services;

            services.AddKeyedSingleton<IAudioDecoder, DefaultOpusDecoder>(GlobalProviderNames.GLOBAL_AUDIO_DECODER);
            RegisterVad(services, config, GlobalProviderNames.GLOBAL_VAD);
            RegisterAsr(services, config, GlobalProviderNames.GLOBAL_ASR);
            RegisterPunctuation(services, config, GlobalProviderNames.GLOBAL_PUNCTUATION);
            RegisterLlm(services, config, GlobalProviderNames.GLOBAL_LLM);
            RegisterMemory(services, config, GlobalProviderNames.GLOBAL_MEMORY);
            RegisterTts(services, config, GlobalProviderNames.GLOBAL_TTS);
            services.AddKeyedSingleton<IAudioEncoder, DefaultOpusEncoder>(GlobalProviderNames.GLOBAL_AUDIO_ENCODER);

            services.AddSingleton<ProviderManager>();
        }

        public bool BuildComponent(IServiceProvider serviceProvider)
        {
            IList<IProvider> providers = new List<IProvider>
            {
                serviceProvider.GetRequiredKeyedService<IAudioDecoder>(GlobalProviderNames.GLOBAL_AUDIO_DECODER),
                serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR),
                serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD),
                serviceProvider.GetRequiredKeyedService<IPunctuation>(GlobalProviderNames.GLOBAL_PUNCTUATION),
                serviceProvider.GetRequiredKeyedService<IMemory>(GlobalProviderNames.GLOBAL_MEMORY),
                serviceProvider.GetRequiredKeyedService<ILlm>(GlobalProviderNames.GLOBAL_LLM),
                serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS),
                serviceProvider.GetRequiredKeyedService<IAudioEncoder>(GlobalProviderNames.GLOBAL_AUDIO_ENCODER)
            };

            foreach (IProvider provider in providers)
            {
                if (!provider.Build())
                {
                    this._logger.LogError("Failed to build {modelName} provider.", provider.ModelName);
                    return false;
                }
            }
            return true;
        }

        public async Task InitializePrivateConfig(Session session)
        {
            try
            {
                Kernel privateKernel = this._globalKernel.Clone();
                privateKernel.Data.Add("session", session);
                session.SetKernel(privateKernel);

                PrivateModelsConfig? privateModelsConfig = null;
                ManageApiClient? manageApiClient = this._serviceProvider.GetService<ManageApiClient>();

                if (manageApiClient is null)
                {
                    this._logger.LogInformation("Remote service is unavailable or not configured, skipping private models config loading for device: {deviceId} with session: {sessionId}.", session.DeviceId, session.SessionId);
                    return;
                }

                privateModelsConfig = await manageApiClient.LoadConfigFromApi(session.DeviceId, session.SessionId);

                if (privateModelsConfig is null)
                {
                    this._logger.LogInformation("The device: {deviceId} with session: {sessionId} has not been configured with privatization settings and will use global providers.", session.DeviceId, session.SessionId);
                    return;
                }

                PrivateProvider privateProvider = new PrivateProvider();

                if (privateModelsConfig.VadSetting is not null)
                {
                    IVad privateVad = this.RegisterVad(privateModelsConfig.VadSetting);
                    privateProvider.InitializeVad(privateVad);

                    this._logger.LogInformation("Private VAD {modeName} model initialized for device: {deviceId} with session: {sessionId}.", privateModelsConfig.VadSetting.ModelName, session.DeviceId, session.SessionId);
                }

                if (privateModelsConfig.AsrSetting is not null)
                {
                    IAsr privateAsr = this.RegisterAsr(privateModelsConfig.AsrSetting);
                    privateProvider.InitializeAsr(privateAsr);

                    this._logger.LogInformation("Private ASR {modeName} model initialized for device: {deviceId} with session: {sessionId}.", privateModelsConfig.AsrSetting.ModelName, session.DeviceId, session.SessionId);
                }

                if (privateModelsConfig.LlmSetting is not null)
                {
                    privateProvider.InitializeLlm(privateModelsConfig.Prompt, privateModelsConfig.UseStreaming, privateModelsConfig.SummaryMemory, privateModelsConfig.LlmModelName);

                    if (!string.IsNullOrEmpty(privateModelsConfig.Prompt))
                    {
                        session.Dialogues.Clear();
                        Dialogue initDialogue = new Dialogue(session.DeviceId, session.SessionId, AuthorRole.System, privateModelsConfig.Prompt);
                        session.Dialogues.Add(initDialogue);
                    }
                    else
                    {
                        Dialogue initDialogue = new Dialogue(session.DeviceId, session.SessionId, AuthorRole.System, this._config.Prompt);
                        session.Dialogues.Add(initDialogue);
                    }

                    if (!string.IsNullOrEmpty(privateModelsConfig.SummaryMemory))
                    {
                        Dialogue summaryMemoryDialogue = new Dialogue(session.DeviceId, session.SessionId, AuthorRole.System, privateModelsConfig.SummaryMemory);
                        session.Dialogues.Add(summaryMemoryDialogue);
                    }

                    this._logger.LogInformation("Private LLM {modeName} model initialized for device: {deviceId} with session: {sessionId}.", privateModelsConfig.LlmSetting.ModelName, session.DeviceId, session.SessionId);
                }

                if (privateModelsConfig.TtsSetting is not null)
                {
                    ITts privateTts = this.RegisterTts(privateModelsConfig.TtsSetting);
                    privateProvider.InitializeTts(privateTts);

                    this._logger.LogInformation("Private TTS {modeName} model initialized for device: {deviceId} with session: {sessionId}.", privateModelsConfig.TtsSetting.ModelName, session.DeviceId, session.SessionId);

                    IAudioEncoder audioEncoder = new DefaultOpusEncoder(privateTts.GetTtsSampleRate(), this._config.AudioSetting, this._logger);
                    audioEncoder.Build();
                    privateProvider.InitializeAudioEncoder(audioEncoder);
                    this._logger.LogInformation("Private Audio Encoder initialized for device: {deviceId} with session: {sessionId}.", session.DeviceId, session.SessionId);

                }

                this.RegisterMCP(session);

                session.PrivateProvider = privateProvider;
            }
            catch (DeviceNotFoundException)
            {
                session.IsDeviceBinded = false;
                session.PrivateProvider = null;
            }
            catch (DeviceBindException deviceBindException)
            {
                session.IsDeviceBinded = false;
                session.PrivateProvider = null;
                session.BindCode = deviceBindException.BindCode;
            }
            catch (Exception ex)
            {
                session.IsDeviceBinded = false;
                session.PrivateProvider = null;
                this._logger.LogError(ex, "Failed to load private models config for device: {deviceId} with session: {sessionId}.", session.DeviceId, session.SessionId);
            }
        }

        public async Task SaveMemoryAsync(Session session)
        {
            var dialogues = session.Dialogues.Where(d => d.Role == AuthorRole.User || d.Role == AuthorRole.Assistant).ToList();
            if (dialogues.Any())
            {
                //todo: save mempry
                ManageApiClient? manageApiClient = this._serviceProvider.GetService<ManageApiClient>();
                if (manageApiClient is not null)
                {
                    try
                    {
                        await manageApiClient.SaveMemoryAsync(session.DeviceId, session.SessionId, dialogues);
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
            IList<IProvider> providers = new List<IProvider>
            {
                serviceProvider.GetRequiredKeyedService<IAudioDecoder>(GlobalProviderNames.GLOBAL_AUDIO_DECODER),
                serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR),
                serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD),
                serviceProvider.GetRequiredKeyedService<IPunctuation>(GlobalProviderNames.GLOBAL_PUNCTUATION),
                serviceProvider.GetRequiredKeyedService<IMemory>(GlobalProviderNames.GLOBAL_MEMORY),
                serviceProvider.GetRequiredKeyedService<ILlm>(GlobalProviderNames.GLOBAL_LLM),
                serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS),
                serviceProvider.GetRequiredKeyedService<IAudioEncoder>(GlobalProviderNames.GLOBAL_AUDIO_ENCODER)
            };

            foreach (IProvider provider in providers)
            {
                provider.Dispose();
            }
        }

        #region Register providers
        #region VAD
        private IVad RegisterVad(ModelSetting vadSetting)
        {
            switch (vadSetting.ModelName)
            {
                case "silero":
                    Silero silero = new Silero(vadSetting, this._logger);
                    silero.Build();
                    return silero;
                case "webrtc":
                    WebRtc webrtc = new WebRtc(vadSetting, this._logger);
                    webrtc.Build();
                    return webrtc;
                default:
                    throw new ModelBuildException("Invalid vad model.");
            }
        }
        private static void RegisterVad(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            switch (config.VadSetting.ModelName.ToLower())
            {
                case "silero":
                    services.AddKeyedSingleton<IVad, Silero>(key); break;
                case "webrtc":
                    services.AddKeyedSingleton<IVad, WebRtc>(key); break;
                default:
                    throw new ModelBuildException("Invalid vad model.");
            }
        }
        #endregion

        #region ASR
        private IAsr RegisterAsr(ModelSetting asrSetting)
        {
            switch (asrSetting.ModelName.ToLower())
            {
                case "sense-voice":
                    SenseVoice senseVoice = new SenseVoice(asrSetting, this._logger);
                    senseVoice.Build();
                    return senseVoice;
                case "paraformer":
                    Paraformer paraformer = new Paraformer(asrSetting, this._logger);
                    paraformer.Build();
                    return paraformer;
                default:
                    throw new ModelBuildException("Invalid asr model.");
            }
        }
        private static void RegisterAsr(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            switch (config.AsrSetting.ModelName.ToLower())
            {
                case "sense-voice":
                    services.AddKeyedSingleton<IAsr, SenseVoice>(key); break;
                case "paraformer":
                    services.AddKeyedSingleton<IAsr, Paraformer>(key); break;
                default:
                    throw new ModelBuildException("Invalid asr model.");
            }
        }
        #endregion

        #region Punctuation
        private static void RegisterPunctuation(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            switch (config.PunctuationSetting.ModelName.ToLower())
            {
                case "ct-transformer":
                    services.AddKeyedSingleton<IPunctuation, CtTransformer>(key); break;
                default:
                    throw new ModelBuildException("Invalid punctuation model.");
            }
        }
        #endregion

        #region LLM
        private static void RegisterLlm(IServiceCollection services, XiaoZhiConfig config, string key)
        {

            int index = 0;
            foreach (var llmSetting in config.LlmSettings)
            {
                string endPoint = llmSetting.Config.BaseUrl;
                string apiKey = llmSetting.Config.ApiKey;
                string modelId = llmSetting.Config.ModelName;

                switch (llmSetting.ModelName.ToLower())
                {
                    case "qwen":
                    case "doubao":
                    case "deepseek":
                    case "chatglm":
                        services.AddOpenAIChatCompletion(modelId, new Uri(endPoint), apiKey, orgId: "Xiao Zhi", $"LLM_{llmSetting.ModelName}");
                        break;
                    default:
                        throw new ModelBuildException("Invalid llm model.");
                }

                if (index == 0)
                {
                    services.AddOpenAIChatCompletion(modelId, new Uri(endPoint), apiKey, orgId: "Xiao Zhi", $"LLM_{SystemLLMServiceNames.GENERIC_LLM_ID}");
                }

                index++;
            }


            services.AddKeyedSingleton<ILlm, GenericOpenAI>(key);
        }
        #endregion

        #region Memory
        private static void RegisterMemory(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            switch (config.MemorySetting.ModelName.ToLower())
            {
                case "flash-memory":
                    services.AddKeyedSingleton<IMemory, FlashMemory>(key); break;
                case "database":
                    services.AddKeyedSingleton<IMemory, Database>(key); break;
                default:
                    throw new ModelBuildException("Invalid memory model.");
            }
        }
        #endregion

        #region TTS
        private ITts RegisterTts(ModelSetting ttsSetting)
        {
            switch (ttsSetting.ModelName.ToLower())
            {
                case "kokoro":
                    Kokoro kokoro = new Kokoro(ttsSetting, this._logger);
                    kokoro.Build();
                    return kokoro;
                case "huoshan-double-stream":
                    HuoshanDoubleStream huoshanDoubleStream = new HuoshanDoubleStream(ttsSetting, this._logger);
                    huoshanDoubleStream.Build();
                    return huoshanDoubleStream;
                default:
                    throw new ModelBuildException("Invalid asr model.");
            }
        }
        private static void RegisterTts(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            switch (config.TtsSetting.ModelName.ToLower())
            {
                case "kokoro":
                    services.AddKeyedSingleton<ITts, Kokoro>(key); break;
                default:
                    throw new ModelBuildException("Invalid tts model.");
            }
        }
        #endregion

        #region MCP
        public void RegisterMCP(Session session)
        {
            IMcpClient mcpClient = new McpClient(session, this._config, this._logger);
            if (!mcpClient.Build())
            {
                this._logger.LogWarning("Session {sessionId} failed to build MCP client.", session.SessionId);
            }
            else
            {
                IDictionary<string, ISubMcpClient> subMcpClients = mcpClient.GetAllSubMcpClients();
                foreach (var item in subMcpClients)
                {
                    session.Kernel.ImportPluginFromFunctions(item.Key, item.Value.Functions);
                }
                session.SetMcpClient(mcpClient);
            }
        }
        #endregion
        #endregion
    }
}