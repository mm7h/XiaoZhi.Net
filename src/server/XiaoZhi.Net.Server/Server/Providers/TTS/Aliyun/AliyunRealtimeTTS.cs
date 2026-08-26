using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Contexts.Aliyun;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Providers.TTS.Aliyun
{
    /// <summary>
    /// 用于 Qwen-Audio-TTS 和 CosyVoice 的阿里云实时语音合成提供者。
    /// 一轮回复对应一个合成任务，后续文本片段会持续发送到同一任务。
    /// </summary>
    internal sealed class AliyunRealtimeTTS : BaseProvider<AliyunRealtimeTTS, ModelSetting>, ITts
    {
        private readonly SemaphoreSlim _turnLock = new(1, 1);

        private ITtsEventCallback? _ttsEventCallback;
        private WebSocketClient? _webSocketClient;
        private AliyunRealtimeTtsOptions? _options;
        private AliyunRealtimeTtsTurnSession? _activeTurn;

        public AliyunRealtimeTTS(ILogger<AliyunRealtimeTTS> logger) : base(logger)
        {
        }

        public override string ProviderType => "tts";

        public override string ModelName => nameof(AliyunRealtimeTTS);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                string apiKey = modelSetting.Config.GetConfigValueOrDefault("ApiKey", string.Empty);
                string modelName = modelSetting.Config.GetConfigValueOrDefault("ModelName", "cosyvoice-v3-flash");
                string voice = modelSetting.Config.GetConfigValueOrDefault("Voice", string.Empty);
                string format = modelSetting.Config.GetConfigValueOrDefault("Format", "pcm");

                if (string.IsNullOrWhiteSpace(apiKey)
                    || string.IsNullOrWhiteSpace(voice)
                    || !IsSupportedRealtimeTtsModel(modelName)
                    || !format.Equals("pcm", StringComparison.OrdinalIgnoreCase))
                {
                    this.Logger.LogWarning(
                        Lang.AliyunRealtimeTTS_Build_ConfigInvalid);
                    return false;
                }

                int sampleRate = modelSetting.Config.GetConfigValueOrDefault("SampleRate", 24000);
                if (sampleRate is not (8000 or 16000 or 22050 or 24000 or 44100 or 48000))
                {
                    this.Logger.LogWarning(Lang.AliyunRealtimeTTS_Build_UnsupportedSampleRate, sampleRate);
                    return false;
                }

                int volume = modelSetting.Config.GetConfigValueOrDefault("Volume", 50);
                float rate = modelSetting.Config.GetConfigValueOrDefault("Rate", 1.0F);
                float pitch = modelSetting.Config.GetConfigValueOrDefault("Pitch", 1.0F);
                if (volume is < 0 or > 100 || rate is < 0.5F or > 2.0F || pitch is < 0.5F or > 2.0F)
                {
                    this.Logger.LogWarning(Lang.AliyunRealtimeTTS_Build_InvalidAudioParameter);
                    return false;
                }

                string endpoint = this.ResolveEndpoint(
                    modelSetting.Config.GetConfigValueOrDefault<string?>("Endpoint"),
                    modelSetting.Config.GetConfigValueOrDefault("Region", "cn-beijing"),
                    modelSetting.Config.GetConfigValueOrDefault<string?>("WorkspaceId"));
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    return false;
                }

                string[] languageHints = (modelSetting.Config.GetConfigValueOrDefault<string?>("LanguageHints") ?? string.Empty)
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                this._options = new AliyunRealtimeTtsOptions(
                    endpoint,
                    apiKey,
                    modelName,
                    voice,
                    sampleRate,
                    volume,
                    rate,
                    pitch,
                    languageHints,
                    modelSetting.Config.GetConfigValueOrDefault<string?>("Instruction"),
                    Math.Clamp(modelSetting.Config.GetConfigValueOrDefault("ConnectionTimeoutSeconds", 5), 1, 60),
                    Math.Clamp(modelSetting.Config.GetConfigValueOrDefault("ResponseTimeoutSeconds", 30), 1, 120));

                this._webSocketClient?.Dispose();
                this._webSocketClient = new WebSocketClient(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Authorization"] = $"Bearer {apiKey}"
                });

                this.Logger.LogInformation(Lang.AliyunRealtimeTTS_Build_Built, modelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.AliyunRealtimeTTS_Build_Failed);
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
            this.AbortSynchronously();
            this._ttsEventCallback = null;
            base.UnregisterDevice(deviceId, sessionId);
        }

        public int GetTtsSampleRate() => this._options?.SampleRate ?? 24000;

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new InvalidOperationException(Lang.AliyunRealtimeTTS_Synthesis_NotRegistered);
            }

            AliyunRealtimeTtsSegment segment = AliyunRealtimeTtsSegment.From(workflow.Data);
            if (string.IsNullOrWhiteSpace(segment.Content))
            {
                return;
            }

            AliyunRealtimeTtsTurnSession? turn;
            bool isNewTurn = false;
            Task? finishTask = null;
            await this._turnLock.WaitAsync(token);
            try
            {
                AliyunRealtimeTtsOptions options = this._options
                    ?? throw new InvalidOperationException(Lang.AliyunRealtimeTTS_Synthesis_NotBuilt);
                WebSocketClient webSocketClient = this._webSocketClient
                    ?? throw new InvalidOperationException(Lang.AliyunRealtimeTTS_Synthesis_ClientNotInitialized);

                turn = this._activeTurn;
                if (segment.IsFirstSegment)
                {
                    if (turn is not null)
                    {
                        await turn.AbortAsync();
                        turn.Dispose();
                    }

                    turn = new AliyunRealtimeTtsTurnSession(
                        webSocketClient,
                        options,
                        this._ttsEventCallback,
                        this.OnTurnFaulted);
                    this._activeTurn = turn;
                    isNewTurn = true;
                }

                if (turn is null)
                {
                    throw new InvalidOperationException(Lang.AliyunRealtimeTTS_Synthesis_MissingFirstSegment);
                }

                if (isNewTurn)
                {
                    await turn.StartAsync(token);
                    this._ttsEventCallback?.OnBeforeProcessing(
                        segment.Content,
                        isFirstSegment: true,
                        isLastSegment: segment.IsLastSegment);
                }

                await turn.AppendTextAsync(segment, token);
                if (segment.IsLastSegment)
                {
                    finishTask = turn.FinishAsync(token);
                }
            }
            catch (OperationCanceledException)
            {
                if (this._activeTurn is not null)
                {
                    await this.StopActiveTurnAsync(this._activeTurn);
                }

                throw;
            }
            catch (Exception ex)
            {
                if (this._activeTurn is not null)
                {
                    this.NotifyTurnFailed(this._activeTurn, ex);
                    await this.StopActiveTurnAsync(this._activeTurn);
                }

                throw;
            }
            finally
            {
                this._turnLock.Release();
            }

            if (finishTask is null)
            {
                return;
            }

            try
            {
                await finishTask;
                this._ttsEventCallback?.OnProcessed(
                    segment.Content,
                    isFirstSegment: segment.IsFirstSegment,
                    isLastSegment: true,
                    TtsGenerateResult.Success);
            }
            catch (OperationCanceledException)
            {
                await this.StopTurnAfterCompletionAsync(turn!);
                throw;
            }
            catch (Exception ex)
            {
                this.NotifyTurnFailed(turn!, ex);
                await this.StopTurnAfterCompletionAsync(turn!);
                throw;
            }

            await this.StopTurnAfterCompletionAsync(turn!);
        }

        public override void Dispose()
        {
            this.AbortSynchronously();
            this._webSocketClient?.Dispose();
            this._turnLock.Dispose();
        }

        private async Task StopTurnAfterCompletionAsync(AliyunRealtimeTtsTurnSession turn)
        {
            await this._turnLock.WaitAsync();
            try
            {
                if (ReferenceEquals(this._activeTurn, turn))
                {
                    this._activeTurn = null;
                }
            }
            finally
            {
                this._turnLock.Release();
                turn.Dispose();
            }
        }

        private async Task StopActiveTurnAsync(AliyunRealtimeTtsTurnSession turn)
        {
            if (ReferenceEquals(this._activeTurn, turn))
            {
                this._activeTurn = null;
            }

            await turn.AbortAsync();
            turn.Dispose();
        }

        private void OnTurnFaulted(AliyunRealtimeTtsTurnSession turn, Exception ex)
        {
            this.NotifyTurnFailed(turn, ex);
        }

        private void NotifyTurnFailed(AliyunRealtimeTtsTurnSession turn, Exception ex)
        {
            if (!turn.TryMarkFailureReported())
            {
                return;
            }

            this.Logger.LogError(ex, Lang.AliyunRealtimeTTS_Task_Failed, turn.TaskId);
            this._ttsEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
        }

        private void AbortSynchronously()
        {
            bool lockTaken = false;
            try
            {
                this._turnLock.Wait();
                lockTaken = true;
                AliyunRealtimeTtsTurnSession? turn = this._activeTurn;
                this._activeTurn = null;
                if (turn is not null)
                {
                    turn.AbortAsync().GetAwaiter().GetResult();
                    turn.Dispose();
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogDebug(ex, Lang.AliyunRealtimeTTS_Abort_Failed);
            }
            finally
            {
                if (lockTaken)
                {
                    this._turnLock.Release();
                }
            }
        }

        private string ResolveEndpoint(string? endpoint, string region, string? workspaceId)
        {
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
                    && uri.Scheme == Uri.UriSchemeWss)
                {
                    return uri.ToString();
                }

                this.Logger.LogWarning(Lang.AliyunRealtimeTTS_Build_EndpointInvalid);
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                string? workspaceEndpoint = region.ToLowerInvariant() switch
                {
                    "cn-beijing" => $"wss://{workspaceId}.cn-beijing.maas.aliyuncs.com/api-ws/v1/inference",
                    "ap-southeast-1" => $"wss://{workspaceId}.ap-southeast-1.maas.aliyuncs.com/api-ws/v1/inference",
                    _ => null
                };
                if (workspaceEndpoint is null)
                {
                    return this.LogUnsupportedRegion(region);
                }

                if (Uri.TryCreate(workspaceEndpoint, UriKind.Absolute, out Uri? workspaceUri)
                    && workspaceUri.Scheme == Uri.UriSchemeWss)
                {
                    return workspaceUri.ToString();
                }

                this.Logger.LogWarning(Lang.AliyunRealtimeTTS_Build_EndpointInvalid);
                return string.Empty;
            }

            return region.ToLowerInvariant() switch
            {
                "cn-beijing" => "wss://dashscope.aliyuncs.com/api-ws/v1/inference",
                "ap-southeast-1" => "wss://dashscope-intl.aliyuncs.com/api-ws/v1/inference",
                _ => this.LogUnsupportedRegion(region)
            };
        }

        private string LogUnsupportedRegion(string region)
        {
            this.Logger.LogWarning(Lang.AliyunRealtimeTTS_Build_UnsupportedRegion, region);
            return string.Empty;
        }

        private static bool IsSupportedRealtimeTtsModel(string modelName) =>
            modelName.StartsWith("cosyvoice-", StringComparison.OrdinalIgnoreCase)
            || (modelName.StartsWith("qwen-audio-", StringComparison.OrdinalIgnoreCase)
                && modelName.Contains("tts", StringComparison.OrdinalIgnoreCase));
    }
}
