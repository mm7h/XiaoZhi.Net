using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Contexts.Aliyun;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Aliyun
{
    /// <summary>
    /// 通过 HTTP 调用阿里云 Qwen-Audio-TTS 与 CosyVoice 非实时语音合成。
    /// 支持返回完整音频 URL 的非流式模式和 SSE 流式模式。
    /// </summary>
    internal sealed class AliyunHttpTTS : BaseProvider<AliyunHttpTTS, ModelSetting>, ITts
    {
        private const string FlurlClientName = nameof(AliyunHttpTTS);
        private const string SpeechSynthesizerPath = "/api/v1/services/audio/tts/SpeechSynthesizer";

        private readonly IAudioEditor _audioEditor;
        private readonly IFlurlClientCache _flurlClientCache;

        private ITtsEventCallback? _ttsEventCallback;
        private AliyunHttpTtsOptions? _options;
        private AudioSavingConfig? _audioSavingConfig;

        public AliyunHttpTTS(
            IAudioEditor audioEditor,
            [FromKeyedServices(FlurlClientName)] IFlurlClientCache flurlClientCache,
            ILogger<AliyunHttpTTS> logger) : base(logger)
        {
            this._audioEditor = audioEditor;
            this._flurlClientCache = flurlClientCache;
        }

        public override string ProviderType => "tts";

        public override string ModelName => nameof(AliyunHttpTTS);

        public override bool Build(ModelSetting modelSetting)
        {
            this._options = null;
            try
            {
                string apiKey = modelSetting.Config.GetConfigValueOrDefault("ApiKey", string.Empty);
                string workspaceId = modelSetting.Config.GetConfigValueOrDefault("WorkspaceId", string.Empty);
                string modelName = modelSetting.Config.GetConfigValueOrDefault("ModelName", "cosyvoice-v3-flash");
                string voice = modelSetting.Config.GetConfigValueOrDefault("Voice", string.Empty);
                string format = modelSetting.Config.GetConfigValueOrDefault("Format", "pcm");

                if (string.IsNullOrWhiteSpace(apiKey)
                    || string.IsNullOrWhiteSpace(voice)
                    || !IsSupportedHttpTtsModel(modelName)
                    || !format.Equals("pcm", StringComparison.OrdinalIgnoreCase))
                {
                    this.Logger.LogWarning(Lang.AliyunHttpTTS_Build_ConfigInvalid);
                    return false;
                }

                string? endpoint = this.ResolveEndpoint(
                    modelSetting.Config.GetConfigValueOrDefault<string?>("Endpoint"),
                    workspaceId);
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    return false;
                }

                int sampleRate = modelSetting.Config.GetConfigValueOrDefault("SampleRate", 24000);
                if (sampleRate is not (8000 or 16000 or 22050 or 24000 or 44100 or 48000))
                {
                    this.Logger.LogWarning(Lang.AliyunHttpTTS_Build_UnsupportedSampleRate, sampleRate);
                    return false;
                }

                int volume = modelSetting.Config.GetConfigValueOrDefault("Volume", 50);
                float rate = modelSetting.Config.GetConfigValueOrDefault("Rate", 1.0F);
                float pitch = modelSetting.Config.GetConfigValueOrDefault("Pitch", 1.0F);
                if (volume is < 0 or > 100 || rate is < 0.5F or > 2.0F || pitch is < 0.5F or > 2.0F)
                {
                    this.Logger.LogWarning(Lang.AliyunHttpTTS_Build_InvalidAudioParameter);
                    return false;
                }

                string[] languageHints = (modelSetting.Config.GetConfigValueOrDefault<string?>("LanguageHints") ?? string.Empty)
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                this._options = new AliyunHttpTtsOptions(
                    endpoint,
                    apiKey,
                    modelName,
                    voice,
                    modelSetting.Config.GetConfigValueOrDefault("Streaming", false),
                    sampleRate,
                    volume,
                    rate,
                    pitch,
                    languageHints,
                    modelSetting.Config.GetConfigValueOrDefault<string?>("Instruction"),
                    Math.Clamp(modelSetting.Config.GetConfigValueOrDefault("ResponseTimeoutSeconds", 120), 1, 600));

                this.BuildAudioSavingConfig(modelSetting);
                this.Logger.LogInformation(Lang.AliyunHttpTTS_Build_Built, modelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.AliyunHttpTTS_Build_Failed);
                return false;
            }
        }

        public void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback)
        {
            this._ttsEventCallback = callback;
            base.RegisterDevice(deviceId, sessionId);
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            this._ttsEventCallback = null;
            base.UnregisterDevice(deviceId, sessionId);
        }

        public int GetTtsSampleRate() => this._options?.SampleRate ?? 24000;

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new InvalidOperationException(Lang.AliyunHttpTTS_Synthesis_NotRegistered);
            }

            AliyunHttpTtsOptions options = this._options
                ?? throw new InvalidOperationException(Lang.AliyunHttpTTS_Synthesis_NotBuilt);
            OutSegment segment = workflow.Data;
            if (string.IsNullOrWhiteSpace(segment.SentenceId))
            {
                this.Logger.LogWarning(Lang.AliyunHttpTTS_Synthesis_MissingSentenceId);
                return;
            }

            var state = new SynthesisState(this._ttsEventCallback, segment);
            try
            {
                state.OnBeforeProcessing();
                Dictionary<string, object?> request = CreateRequest(options, segment.Content);
                bool synthesized = options.Streaming
                    ? await this.SynthesizeStreamingAsync(options, request, state, token)
                    : await this.SynthesizeNonStreamingAsync(options, request, state, token);
                if (!synthesized || !state.HasAudio)
                {
                    if (synthesized)
                    {
                        this.Logger.LogError(Lang.AliyunHttpTTS_Synthesis_NoAudio);
                    }

                    state.ReportProcessed(TtsGenerateResult.Failed);
                    return;
                }

                await this.SaveAudioFileAsync(workflow.DeviceId, segment.SentenceId, state.PcmData);
                state.OnSentenceEnd();
                state.ReportProcessed(TtsGenerateResult.Success);
            }
            catch (OperationCanceledException)
            {
                state.ReportProcessed(TtsGenerateResult.Aborted);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.AliyunHttpTTS_Synthesis_Failed);
                state.ReportProcessed(TtsGenerateResult.Failed);
                throw;
            }
        }

        public override void Dispose()
        {
            this._ttsEventCallback = null;
        }

        private async Task<bool> SynthesizeNonStreamingAsync(
            AliyunHttpTtsOptions options,
            Dictionary<string, object?> request,
            SynthesisState state,
            CancellationToken token)
        {
            IFlurlClient client = this._flurlClientCache.Get(FlurlClientName);
            using IFlurlResponse response = await client.Request(options.Endpoint)
                .WithHeader("Authorization", $"Bearer {options.ApiKey}")
                .WithTimeout(options.ResponseTimeoutSeconds)
                .AllowAnyHttpStatus()
                .PostJsonAsync(request, cancellationToken: token);

            if (!response.ResponseMessage.IsSuccessStatusCode)
            {
                string body = await response.GetStringAsync();
                this.Logger.LogError(Lang.AliyunHttpTTS_Synthesis_RequestFailed, response.StatusCode, body);
                return false;
            }

            AliyunHttpTtsResponse? synthesisResponse = await response.GetJsonAsync<AliyunHttpTtsResponse>();
            if (IsApiError(synthesisResponse))
            {
                this.LogApiError(synthesisResponse!);
                return false;
            }

            string? audioUrl = synthesisResponse?.Output?.Audio?.Url;
            if (string.IsNullOrWhiteSpace(audioUrl))
            {
                this.Logger.LogError(Lang.AliyunHttpTTS_Synthesis_InvalidResponse);
                return false;
            }

            using IFlurlResponse audioResponse = await client.Request(audioUrl)
                .WithTimeout(options.ResponseTimeoutSeconds)
                .AllowAnyHttpStatus()
                .GetAsync(cancellationToken: token);
            if (!audioResponse.ResponseMessage.IsSuccessStatusCode)
            {
                string body = await audioResponse.GetStringAsync();
                this.Logger.LogError(Lang.AliyunHttpTTS_Synthesis_DownloadFailed, audioResponse.StatusCode, body);
                return false;
            }

            byte[] audioData = await audioResponse.GetBytesAsync();
            state.AppendAudio(audioData);
            return true;
        }

        private async Task<bool> SynthesizeStreamingAsync(
            AliyunHttpTtsOptions options,
            Dictionary<string, object?> request,
            SynthesisState state,
            CancellationToken token)
        {
            IFlurlClient client = this._flurlClientCache.Get(FlurlClientName);
            using IFlurlResponse response = await client.Request(options.Endpoint)
                .WithHeader("Authorization", $"Bearer {options.ApiKey}")
                .WithHeader("X-DashScope-SSE", "enable")
                .WithTimeout(options.ResponseTimeoutSeconds)
                .AllowAnyHttpStatus()
                .PostJsonAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

            if (!response.ResponseMessage.IsSuccessStatusCode)
            {
                string body = await response.GetStringAsync();
                this.Logger.LogError(Lang.AliyunHttpTTS_Synthesis_RequestFailed, response.StatusCode, body);
                return false;
            }

            bool completed = false;
            var eventData = new StringBuilder();
            await using Stream stream = await response.GetStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string? line = await reader.ReadLineAsync(token);
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    if (!this.TryProcessSseEvent(eventData, state, out bool eventCompleted))
                    {
                        return false;
                    }

                    completed |= eventCompleted;
                    eventData.Clear();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (eventData.Length > 0)
                    {
                        eventData.AppendLine();
                    }

                    eventData.Append(line.AsSpan(5).TrimStart());
                }
            }

            if (!this.TryProcessSseEvent(eventData, state, out bool finalEventCompleted))
            {
                return false;
            }

            return completed || finalEventCompleted;
        }

        private bool TryProcessSseEvent(StringBuilder eventData, SynthesisState state, out bool completed)
        {
            completed = false;
            if (eventData.Length == 0)
            {
                return true;
            }

            string payload = eventData.ToString();
            if (payload.Equals("[DONE]", StringComparison.Ordinal))
            {
                return true;
            }

            AliyunHttpTtsResponse? message = JsonHelper.Deserialize<AliyunHttpTtsResponse>(payload);
            if (message is null)
            {
                this.Logger.LogError(Lang.AliyunHttpTTS_Synthesis_InvalidResponse);
                return false;
            }

            if (IsApiError(message))
            {
                this.LogApiError(message);
                return false;
            }

            if (message.Output is null)
            {
                this.Logger.LogError(Lang.AliyunHttpTTS_Synthesis_InvalidResponse);
                return false;
            }

            if (message.Output.Type?.Equals("sentence-begin", StringComparison.OrdinalIgnoreCase) == true)
            {
                state.OnSentenceStart();
            }

            string? encodedAudio = message.Output.Audio?.Data;
            if (!string.IsNullOrWhiteSpace(encodedAudio))
            {
                try
                {
                    state.AppendAudio(Convert.FromBase64String(encodedAudio));
                }
                catch (FormatException ex)
                {
                    this.Logger.LogError(ex, Lang.AliyunHttpTTS_Synthesis_InvalidResponse);
                    return false;
                }
            }

            completed = message.Output.FinishReason?.Equals("stop", StringComparison.OrdinalIgnoreCase) == true;
            return true;
        }

        private void BuildAudioSavingConfig(ModelSetting modelSetting)
        {
            this._audioSavingConfig = modelSetting.Config.GetConfigValueOrDefault("FileSavingOption", new AudioSavingConfig(false));
            if (this._audioSavingConfig.SaveFile && !Directory.Exists(this._audioSavingConfig.SavePath))
            {
                Directory.CreateDirectory(this._audioSavingConfig.SavePath);
            }
        }

        private async Task SaveAudioFileAsync(string deviceId, string fileName, float[] audioData)
        {
            if (this._audioSavingConfig is null || !this._audioSavingConfig.SaveFile)
            {
                return;
            }

            string savedFileName = $"{this.ProviderType}_{fileName}.{this._audioSavingConfig.Format}";
            string savePath = Path.Combine(this._audioSavingConfig.SavePath, savedFileName);
            try
            {
                bool saved = await this._audioEditor.SaveAudioFileAsync(
                    savePath,
                    audioData,
                    this.GetTtsSampleRate(),
                    1,
                    this._audioSavingConfig.BitRate);
                if (saved)
                {
                    this.Logger.LogInformation(Lang.AliyunHttpTTS_SaveAudioFile_FileSaved, savedFileName, deviceId);
                }
                else
                {
                    this.Logger.LogWarning(Lang.AliyunHttpTTS_SaveAudioFile_SaveFailed, savedFileName, deviceId);
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.AliyunHttpTTS_SaveAudioFile_SaveFailed, savedFileName, deviceId);
            }
        }

        private string? ResolveEndpoint(string? endpoint, string workspaceId)
        {
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri)
                    && endpointUri.Scheme == Uri.UriSchemeHttps)
                {
                    return endpointUri.ToString();
                }

                this.Logger.LogWarning(Lang.AliyunHttpTTS_Build_EndpointInvalid);
                return null;
            }

            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                this.Logger.LogWarning(Lang.AliyunHttpTTS_Build_EndpointInvalid);
                return null;
            }

            string resolvedEndpoint = $"https://{workspaceId}.cn-beijing.maas.aliyuncs.com{SpeechSynthesizerPath}";
            if (Uri.TryCreate(resolvedEndpoint, UriKind.Absolute, out _))
            {
                return resolvedEndpoint;
            }

            this.Logger.LogWarning(Lang.AliyunHttpTTS_Build_EndpointInvalid);
            return null;
        }

        private static Dictionary<string, object?> CreateRequest(AliyunHttpTtsOptions options, string text)
        {
            var input = new Dictionary<string, object?>
            {
                ["text"] = text,
                ["voice"] = options.Voice,
                ["format"] = "pcm",
                ["sample_rate"] = options.SampleRate,
                ["volume"] = options.Volume,
                ["rate"] = options.Rate,
                ["pitch"] = options.Pitch
            };
            if (options.LanguageHints.Length > 0)
            {
                input["language_hints"] = options.LanguageHints;
            }

            if (!string.IsNullOrWhiteSpace(options.Instruction))
            {
                input["instruction"] = options.Instruction;
            }

            return new Dictionary<string, object?>
            {
                ["model"] = options.ModelName,
                ["input"] = input
            };
        }

        private static bool IsApiError(AliyunHttpTtsResponse? response) =>
            !string.IsNullOrWhiteSpace(response?.Code)
            && !response.Code.Equals("0", StringComparison.OrdinalIgnoreCase)
            && !response.Code.Equals("200", StringComparison.OrdinalIgnoreCase);

        private void LogApiError(AliyunHttpTtsResponse response)
        {
            this.Logger.LogError(
                Lang.AliyunHttpTTS_Synthesis_ApiError,
                response.RequestId ?? string.Empty,
                response.Code ?? string.Empty,
                response.Message ?? string.Empty);
        }

        private static bool IsSupportedHttpTtsModel(string modelName) =>
            modelName.StartsWith("cosyvoice-", StringComparison.OrdinalIgnoreCase)
            || (modelName.StartsWith("qwen-audio-", StringComparison.OrdinalIgnoreCase)
                && modelName.Contains("-tts", StringComparison.OrdinalIgnoreCase));

        private sealed class SynthesisState
        {
            private readonly ITtsEventCallback? _callback;
            private readonly OutSegment _segment;
            private readonly List<float> _pcm = [];
            private bool _processed;
            private bool _sentenceStarted;

            public SynthesisState(ITtsEventCallback? callback, OutSegment segment)
            {
                this._callback = callback;
                this._segment = segment;
            }

            public bool HasAudio => this._pcm.Count > 0;

            public float[] PcmData => this._pcm.ToArray();

            public void OnBeforeProcessing() =>
                this._callback?.OnBeforeProcessing(this._segment.Content, this._segment.IsFirstSegment, this._segment.IsLastSegment);

            public void OnSentenceStart()
            {
                if (this._sentenceStarted)
                {
                    return;
                }

                this._sentenceStarted = true;
                this._callback?.OnSentenceStart(this._segment.Content, this._segment.Emotion, this._segment.SentenceId!);
            }

            public void AppendAudio(byte[] audioData)
            {
                float[] pcm = audioData.PcmBytesToFloat(16);
                if (pcm.Length == 0)
                {
                    return;
                }

                this.OnSentenceStart();
                this._pcm.AddRange(pcm);
                this._callback?.OnProcessing(pcm, isFirstFrame: false, isLastFrame: false);
            }

            public void OnSentenceEnd()
            {
                if (this._sentenceStarted)
                {
                    this._callback?.OnSentenceEnd(this._segment.Content, this._segment.Emotion, this._segment.SentenceId!);
                }
            }

            public void ReportProcessed(TtsGenerateResult result)
            {
                if (this._processed)
                {
                    return;
                }

                this._processed = true;
                this._callback?.OnProcessed(this._segment.Content, this._segment.IsFirstSegment, this._segment.IsLastSegment, result);
            }
        }
    }
}
