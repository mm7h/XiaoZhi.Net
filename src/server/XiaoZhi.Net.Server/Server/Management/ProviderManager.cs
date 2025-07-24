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
using XiaoZhi.Net.Server.Services;

namespace XiaoZhi.Net.Server.Management
{
    internal sealed class ProviderManager
    {
        private readonly ManageApiClient _manageApiClient;
        private readonly ILogger<ProviderManager> _logger;

        private const string GLOBAL_AUDIO_DECODER = "GlobalAudioDecoder";
        private const string GLOBAL_ASR = "GlobalAsr";
        private const string GLOBAL_VAD = "GlobalVad";
        private const string GLOBAL_PUNCTUATION = "GlobalPunctuation";
        private const string GLOBAL_MEMORY = "GlobalMemory";
        private const string GLOBAL_LLM = "GlobalLlm";
        private const string GLOBAL_TTS = "GlobalTts";
        private const string GLOBAL_AUDIO_ENCODER = "GlobalAudioEncoder";


        public ProviderManager(ManageApiClient manageApiClient, ILogger<ProviderManager> logger)
        {
            this._manageApiClient = manageApiClient;
            this._logger = logger;
        }

        public static void RegisterServices(HostApplicationBuilder builder, XiaoZhiConfig config)
        {
            IServiceCollection services = builder.Services;

            services.AddKeyedSingleton<IAudioDecoder, DefaultOpusDecoder>(GLOBAL_AUDIO_DECODER);
            RegisterVad(services, config, GLOBAL_VAD);
            RegisterAsr(services, config, GLOBAL_ASR);
            RegisterPunctuation(services, config, GLOBAL_PUNCTUATION);
            RegisterLlm(services, config, GLOBAL_LLM);
            RegisterMemory(services, config, GLOBAL_MEMORY);
            RegisterTts(services, config, GLOBAL_TTS);
            services.AddKeyedSingleton<IAudioEncoder, DefaultOpusEncoder>(GLOBAL_AUDIO_ENCODER);

            services.AddSingleton<ProviderManager>();
        }

        public bool BuildComponent(IServiceProvider serviceProvider)
        {
            IList<IProvider> providers = new List<IProvider>
            {
                serviceProvider.GetRequiredKeyedService<IAudioDecoder>(GLOBAL_AUDIO_DECODER),
                serviceProvider.GetRequiredKeyedService<IAsr>(GLOBAL_ASR),
                serviceProvider.GetRequiredKeyedService<IVad>(GLOBAL_VAD),
                serviceProvider.GetRequiredKeyedService<IPunctuation>(GLOBAL_PUNCTUATION),
                serviceProvider.GetRequiredKeyedService<IMemory>(GLOBAL_MEMORY),
                serviceProvider.GetRequiredKeyedService<ILlm>(GLOBAL_LLM),
                serviceProvider.GetRequiredKeyedService<ITts>(GLOBAL_TTS),
                serviceProvider.GetRequiredKeyedService<IAudioEncoder>(GLOBAL_AUDIO_ENCODER)
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
                PrivateModelsConfig? privateModelsConfig = await this._manageApiClient.LoadConfigFromApi(session.DeviceId, session.SessionId);
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
                }

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

            }
        }

        public void Dispose(IServiceProvider serviceProvider)
        {
            IList<IProvider> providers = new List<IProvider>
            {
                serviceProvider.GetRequiredKeyedService<IAudioDecoder>(GLOBAL_AUDIO_DECODER),
                serviceProvider.GetRequiredKeyedService<IAsr>(GLOBAL_ASR),
                serviceProvider.GetRequiredKeyedService<IVad>(GLOBAL_VAD),
                serviceProvider.GetRequiredKeyedService<IPunctuation>(GLOBAL_PUNCTUATION),
                serviceProvider.GetRequiredKeyedService<IMemory>(GLOBAL_MEMORY),
                serviceProvider.GetRequiredKeyedService<ILlm>(GLOBAL_LLM),
                serviceProvider.GetRequiredKeyedService<ITts>(GLOBAL_TTS),
                serviceProvider.GetRequiredKeyedService<IAudioEncoder>(GLOBAL_AUDIO_ENCODER)
            };

            foreach (IProvider provider in providers)
            {
                provider.Dispose();
            }
        }

        #region Register providers
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

        private static void RegisterLlm(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            ModelSetting llmSetting = config.LlmSettings.First();
            string endPoint = llmSetting.Config.BaseUrl;
            string apiKey = llmSetting.Config.ApiKey;
            string modelId = llmSetting.Config.ModelName;

            switch (llmSetting.ModelName.ToLower())
            {
                case "qwen":
                case "doubao":
                case "deepseek":
                case "chatglm":
                    services.AddOpenAIChatCompletion(modelId, new Uri(endPoint), apiKey, orgId: "Xiao Zhi", SystemLLMServiceNames.GENERIC_LLM_ID);
                    services.AddKeyedSingleton<ILlm, GenericOpenAI>(key);
                    break;
                default:
                    throw new ModelBuildException("Invalid llm model.");
            }
        }

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
    }
}
