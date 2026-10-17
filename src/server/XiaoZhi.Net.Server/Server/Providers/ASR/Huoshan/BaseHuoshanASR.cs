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

namespace XiaoZhi.Net.Server.Providers.ASR.Huoshan
{
    /// <summary>
    /// 协调当前活动的火山 ASR 语句。协议和每轮状态由
    /// <see cref="HuoshanAsrUtteranceSession"/> 管理，使 Provider 能在连续语句间
    /// 安全复用同一个 WebSocket 包装对象。
    /// </summary>
    internal abstract class BaseHuoshanASR<TLogger> : BaseProvider<TLogger, ModelSetting>, IAsr
    {
        private readonly SemaphoreSlim _streamLock = new(1, 1);
        private readonly WebSocketClient _webSocketClient = new(null);

        private IAsrEventCallback? _asrEventCallback;
        private HuoshanAsrOptions? _options;
        private HuoshanAsrUtteranceSession? _activeSession;

        protected BaseHuoshanASR(ILogger<TLogger> logger) : base(logger)
        {
        }

        public override string ProviderType => "asr";
        public bool IsStreaming => true;
        protected abstract string ServiceEndpoint { get; }
        protected virtual bool SupportsLanguage => false;

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                string apiKey = modelSetting.Config.GetConfigValueOrDefault("ApiKey", string.Empty);
                string appId = modelSetting.Config.GetConfigValueOrDefault("AppId", string.Empty);
                string accessToken = modelSetting.Config.GetConfigValueOrDefault("AccessToken", string.Empty);
                string resourceId = modelSetting.Config.GetConfigValueOrDefault("ResourceId", string.Empty);
                bool hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
                bool hasLegacyCredentials = !string.IsNullOrWhiteSpace(appId)
                    && !string.IsNullOrWhiteSpace(accessToken);
                if (string.IsNullOrWhiteSpace(resourceId) || (!hasApiKey && !hasLegacyCredentials))
                {
                    this.Logger.LogWarning(Lang.BaseHuoshanASR_Build_ConfigIncomplete, this.ModelName);
                    return false;
                }

                int segmentDurationMs = modelSetting.Config.GetConfigValueOrDefault("SegmentDurationMs", 200);
                if (segmentDurationMs is < 100 or > 1000)
                {
                    this.Logger.LogWarning(Lang.BaseHuoshanASR_Build_SegmentDurationInvalid);
                    return false;
                }

                int packetSizeBytes = GlobalVariables.AudioProcessingSampleRate
                    * GlobalVariables.AudioProcessingChannels
                    * (GlobalVariables.AudioProcessingBitsPerSample / 8)
                    * segmentDurationMs / 1000;
                this._options = new HuoshanAsrOptions(
                    this.ServiceEndpoint,
                    apiKey,
                    appId,
                    accessToken,
                    resourceId,
                    modelSetting.Config.GetConfigValueOrDefault("AsrModelName", "bigmodel"),
                    this.SupportsLanguage
                        ? modelSetting.Config.GetConfigValueOrDefault<string?>("Language")
                        : null,
                    packetSizeBytes,
                    Math.Clamp(modelSetting.Config.GetConfigValueOrDefault("ResponseTimeoutSeconds", 15), 1, 60),
                    modelSetting.Config.GetConfigValueOrDefault("EnableItn", true),
                    modelSetting.Config.GetConfigValueOrDefault("EnablePunc", true),
                    modelSetting.Config.GetConfigValueOrDefault("EnableDdc", true));

                this.Logger.LogInformation(Lang.BaseHuoshanASR_Build_Built, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.BaseHuoshanASR_Build_Failed, this.ModelName);
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
                throw new InvalidOperationException("The Huoshan ASR provider is not registered for this session.");
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

                HuoshanAsrOptions options = this._options
                    ?? throw new InvalidOperationException("The Huoshan ASR provider has not been built.");
                var session = new HuoshanAsrUtteranceSession(
                    this._webSocketClient,
                    options,
                    this.DeviceId,
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
            HuoshanAsrUtteranceSession? session;
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
                    this.Logger.LogError(
                        ex,
                        Lang.BaseHuoshanASR_FinishUtterance_WaitForFinalResultFailed,
                        this.ModelName);
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
                HuoshanAsrUtteranceSession? session = this._activeSession;
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

        private void AbortSynchronously()
        {
            try
            {
                this.AbortUtteranceAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                this.Logger.LogDebug(ex, Lang.BaseHuoshanASR_AbortSynchronously_Failed);
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
