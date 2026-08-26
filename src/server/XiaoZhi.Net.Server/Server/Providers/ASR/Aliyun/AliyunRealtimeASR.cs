using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Providers.ASR.Contexts;

namespace XiaoZhi.Net.Server.Providers.ASR.Aliyun
{
    /// <summary>
    /// 协调当前活动的 DashScope ASR 任务。每轮任务由
    /// <see cref="AliyunRealtimeAsrUtteranceSession"/> 表示；旧连接关闭后，
    /// WebSocket 包装对象仍可供下一轮语句使用。
    /// </summary>
    internal sealed class AliyunRealtimeASR : BaseProvider<AliyunRealtimeASR, ModelSetting>, IAsr
    {
        private readonly SemaphoreSlim _streamLock = new(1, 1);
        private readonly WebSocketClient _webSocketClient = new(null);

        private IAsrEventCallback? _asrEventCallback;
        private AliyunRealtimeAsrOptions? _options;
        private AliyunRealtimeAsrUtteranceSession? _activeSession;

        public AliyunRealtimeASR(ILogger<AliyunRealtimeASR> logger) : base(logger)
        {
        }

        public override string ProviderType => "asr";
        public override string ModelName => nameof(AliyunRealtimeASR);
        public bool IsStreaming => true;

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                string apiKey = modelSetting.Config.GetConfigValueOrDefault("ApiKey", string.Empty);
                string modelName = modelSetting.Config.GetConfigValueOrDefault(
                    "ModelName",
                    "qwen-audio-3.0-asr-flash-streaming");
                if (string.IsNullOrWhiteSpace(apiKey)
                    || (!modelName.Equals("qwen-audio-3.0-asr-flash-streaming", StringComparison.OrdinalIgnoreCase)
                        && !modelName.StartsWith("fun-asr-realtime", StringComparison.OrdinalIgnoreCase)))
                {
                    this.Logger.LogWarning(Lang.AliyunRealtimeASR_Build_ConfigIncomplete);
                    return false;
                }

                string serviceEndpoint = this.ResolveEndpoint(
                    modelSetting.Config.GetConfigValueOrDefault<string?>("Endpoint"),
                    modelSetting.Config.GetConfigValueOrDefault("Region", "cn-beijing"),
                    modelSetting.Config.GetConfigValueOrDefault<string?>("WorkspaceId"));
                if (string.IsNullOrWhiteSpace(serviceEndpoint))
                {
                    return false;
                }

                int segmentDurationMs = modelSetting.Config.GetConfigValueOrDefault("SegmentDurationMs", 100);
                if (segmentDurationMs is < 20 or > 1000)
                {
                    this.Logger.LogWarning(Lang.AliyunRealtimeASR_Build_SegmentDurationInvalid);
                    return false;
                }

                int packetSizeBytes = GlobalVariables.AudioProcessingSampleRate
                    * GlobalVariables.AudioProcessingChannels
                    * (GlobalVariables.AudioProcessingBitsPerSample / 8)
                    * segmentDurationMs / 1000;
                string[] languageHints = (modelSetting.Config.GetConfigValueOrDefault<string?>("LanguageHints")
                    ?? string.Empty)
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                this._options = new AliyunRealtimeAsrOptions(
                    serviceEndpoint,
                    apiKey,
                    modelName,
                    languageHints,
                    packetSizeBytes,
                    Math.Clamp(
                        modelSetting.Config.GetConfigValueOrDefault("ConnectionTimeoutSeconds", 5),
                        1,
                        60),
                    Math.Clamp(
                        modelSetting.Config.GetConfigValueOrDefault("ResponseTimeoutSeconds", 15),
                        1,
                        60));

                this.Logger.LogInformation(Lang.AliyunRealtimeASR_Build_Built, modelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.AliyunRealtimeASR_Build_Failed);
                return false;
            }
        }

        public void RegisterDevice(string deviceId, string sessionId, IAsrEventCallback callback)
        {
            this._asrEventCallback = callback;
            base.RegisterDevice(deviceId, sessionId);
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            this.AbortSynchronously();
            this._asrEventCallback = null;
            base.UnregisterDevice(deviceId, sessionId);
        }

        public async Task ConvertSpeechTextAsync(
            Workflow<float[]> workflow,
            int sampleRate,
            int frameSize,
            CancellationToken token)
        {
            await this.ConvertSpeechTextStreamingAsync(
                    workflow,
                    sampleRate,
                    frameSize,
                    StreamingAsrOperation.Start,
                    token)
                ;
            await this.ConvertSpeechTextStreamingAsync(
                    workflow,
                    sampleRate,
                    frameSize,
                    StreamingAsrOperation.Finish,
                    token)
                ;
        }

        public Task ConvertSpeechTextStreamingAsync(
            Workflow<float[]> workflow,
            int sampleRate,
            int frameSize,
            StreamingAsrOperation operation,
            CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new InvalidOperationException("The Aliyun ASR provider is not registered for this session.");
            }

            return operation switch
            {
                StreamingAsrOperation.Start => this.StartUtteranceAsync(workflow.Data, sampleRate, workflow.TurnId, token),
                StreamingAsrOperation.Audio => this.AppendAudioAsync(workflow.Data, sampleRate, token),
                StreamingAsrOperation.Finish => this.FinishUtteranceAsync(token),
                StreamingAsrOperation.Abort => this.AbortUtteranceAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };
        }

        private async Task StartUtteranceAsync(float[] initialAudio, int sampleRate, long turnId, CancellationToken token)
        {
            await this._streamLock.WaitAsync(token);
            try
            {
                if (this._activeSession is not null)
                {
                    return;
                }

                AliyunRealtimeAsrOptions options = this._options
                    ?? throw new InvalidOperationException("The Aliyun ASR provider has not been built.");
                var session = new AliyunRealtimeAsrUtteranceSession(
                    this._webSocketClient,
                    options,
                    turnId);
                this._activeSession = session;
                try
                {
                    await session.StartAsync(initialAudio, sampleRate, token);
                }
                catch
                {
                    if (ReferenceEquals(this._activeSession, session))
                    {
                        this._activeSession = null;
                    }

                    session.Dispose();
                    throw;
                }
            }
            finally
            {
                this._streamLock.Release();
            }
        }

        private async Task AppendAudioAsync(float[] audioData, int sampleRate, CancellationToken token)
        {
            await this._streamLock.WaitAsync(token);
            try
            {
                this._activeSession?.AppendAudio(audioData, sampleRate);
            }
            finally
            {
                this._streamLock.Release();
            }
        }

        private async Task FinishUtteranceAsync(CancellationToken token)
        {
            AliyunRealtimeAsrUtteranceSession? session;
            Task<string?>? finishTask;
            await this._streamLock.WaitAsync(token);
            try
            {
                session = this._activeSession;
                if (session is null || session.IsAborted || session.IsFinishing)
                {
                    return;
                }

                finishTask = session.FinishAsync(token);
            }
            finally
            {
                this._streamLock.Release();
            }

            string? finalText = null;
            bool failed = false;
            bool shouldNotify = false;
            try
            {
                finalText = await finishTask;
            }
            catch (Exception ex)
            {
                failed = !session.IsAborted;
                if (failed)
                {
                    this.Logger.LogError(ex, Lang.AliyunRealtimeASR_FinishUtterance_WaitForFinalResultFailed);
                }
            }
            finally
            {
                await this._streamLock.WaitAsync();
                try
                {
                    if (ReferenceEquals(this._activeSession, session))
                    {
                        this._activeSession = null;
                        shouldNotify = !session.IsAborted;
                    }
                }
                finally
                {
                    this._streamLock.Release();
                    session.Dispose();
                }
            }

            if (shouldNotify)
            {
                this._asrEventCallback?.OnSpeechTextConverted(
                    session.TurnId,
                    !failed,
                    failed ? string.Empty : finalText ?? string.Empty);
            }
        }

        private async Task AbortUtteranceAsync()
        {
            await this._streamLock.WaitAsync();
            try
            {
                AliyunRealtimeAsrUtteranceSession? session = this._activeSession;
                this._activeSession = null;
                if (session is not null)
                {
                    await session.AbortAsync();
                    session.Dispose();
                }
            }
            finally
            {
                this._streamLock.Release();
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

                this.Logger.LogWarning(Lang.AliyunRealtimeASR_Build_EndpointInvalid);
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

                this.Logger.LogWarning(Lang.AliyunRealtimeASR_Build_EndpointInvalid);
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
            this.Logger.LogWarning(Lang.AliyunRealtimeASR_Build_UnsupportedRegion, region);
            return string.Empty;
        }

        private void AbortSynchronously()
        {
            try
            {
                this.AbortUtteranceAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                this.Logger.LogDebug(ex, Lang.AliyunRealtimeASR_AbortSynchronously_Failed);
            }
        }

        public override void Dispose()
        {
            this.AbortSynchronously();
            this._webSocketClient.Dispose();
            this._streamLock.Dispose();
        }
    }
}
