using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Resources.OnnxModels;
using XiaoZhi.Net.Server.Resources.OnnxModels.VAD;
using XiaoZhi.Net.Server.Resources.OnnxModels.VAD.Models;

namespace XiaoZhi.Net.Server.Providers.VAD.Native
{
    /// <summary>
    /// Silero VAD v4 implementation using ML.NET ONNX Runtime.
    /// </summary>
    internal sealed class SileroNative : BaseProvider<SileroNative, ModelSetting>, IVad
    {
        private readonly IServiceProvider _serviceProvider;

        private IVadOnnxModel? _vadOnnxModel;
        private SileroModelState? _modelState;
        private int _sampleRate;
        private int _silenceThresholdMs;
        private float _threshold;
        private float _thresholdLow;

        private const int REQUIRED_VOICE_FRAMES = 3;
        private const float DEFAULT_THRESHOLD = 0.5f;
        private const float DEFAULT_THRESHOLD_LOW = 0.2f;
        private const int DEFAULT_SILENCE_THRESHOLD_MS = 700;
        private const int SAMPLING_RATE_8K = 8000;
        private const int SAMPLING_RATE_16K = 16000;

        public SileroNative(IServiceProvider serviceProvider, ILogger<SileroNative> logger) : base(logger)
        {
            this._serviceProvider = serviceProvider;
        }

        public override string ProviderType => "vad";
        public override string ModelName => nameof(SileroNative);

        public int FrameSize { get; private set; }

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                this._sampleRate = modelSetting.Config.GetConfigValueOrDefault("SampleRate", SAMPLING_RATE_16K);
                this._silenceThresholdMs = modelSetting.Config.GetConfigValueOrDefault("SilenceThresholdMs", DEFAULT_SILENCE_THRESHOLD_MS);
                this._threshold = modelSetting.Config.GetConfigValueOrDefault("Threshold", DEFAULT_THRESHOLD);
                this._thresholdLow = modelSetting.Config.GetConfigValueOrDefault("ThresholdLow", DEFAULT_THRESHOLD_LOW);

                if (this._sampleRate != SAMPLING_RATE_8K && this._sampleRate != SAMPLING_RATE_16K)
                {
                    this.Logger.LogError("Unsupported sample rate: {sampleRate}. Only 8000 and 16000 are supported.", this._sampleRate);
                    return false;
                }

                this.FrameSize = this._sampleRate == SAMPLING_RATE_16K ? 512 : 256;
                this._modelState = SileroOnnx.CreateModelState(this._sampleRate);

                this._vadOnnxModel = this._serviceProvider.GetRequiredService<IVadOnnxModel>();

                this.Logger.LogInformation("Built the {providerType} model: {modelName} with sample rate: {sampleRate}Hz, threshold: {threshold}, threshold low: {thresholdLow}, silence threshold: {silenceThresholdMs}ms",
                    this.ProviderType, this.ModelName, this._sampleRate, this._threshold, this._thresholdLow, this._silenceThresholdMs);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public override void RegisterDevice(string deviceId, string sessionId)
        {
            base.RegisterDevice(deviceId, sessionId);
            this.Logger.LogDebug("Registered device for Native VAD: {deviceId}, session: {sessionId}", deviceId, sessionId);
        }

        public Task<bool> AnalysisVoiceAsync(Session session, CancellationToken token)
        {
            if (this._modelState is null)
            {
                throw new InvalidOperationException("Please build the VAD provider first by calling Build().");
            }

            if (this._vadOnnxModel is null)
            {
                throw new InvalidOperationException("VAD ONNX model is not initialized.");
            }

            var vadStatus = session.VadStatusContext;

            try
            {
                bool clientHaveVoice = false;

                while (session.AudioPacketContext.VadPacket.GetFrames(this.FrameSize, out float[] chunk))
                {
                    token.ThrowIfCancellationRequested();

                    if (chunk.Length == 0)
                    {
                        continue;
                    }

                    // Run inference using shared singleton model with per-instance state
                    float speechProb = this._vadOnnxModel.Infer(chunk, this._sampleRate, this._modelState);

                    // Dual-threshold hysteresis logic (matching Python silero.py implementation)
                    bool isVoice;
                    if (speechProb >= this._threshold)
                    {
                        isVoice = true;
                    }
                    else if (speechProb <= this._thresholdLow)
                    {
                        isVoice = false;
                    }
                    else
                    {
                        // Between thresholds: maintain previous state (hysteresis)
                        isVoice = vadStatus.LastIsVoice;
                    }

                    // Update last voice state for next iteration (stored in Session.VadStatusContext)
                    vadStatus.LastIsVoice = isVoice;

                    // Update sliding window (stored in Session.VadStatusContext)
                    vadStatus.AddVoiceFrame(isVoice);

                    // Check if enough frames in window are voice
                    clientHaveVoice = vadStatus.CountVoiceFrames() >= REQUIRED_VOICE_FRAMES;

                    // If previously had voice but now doesn't, check silence duration
                    if (vadStatus.HaveVoice && !clientHaveVoice)
                    {
                        long stopDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - vadStatus.HaveVoiceLatestTime;
                        if (stopDuration > this._silenceThresholdMs)
                        {
#if DEBUG
                            this.Logger.LogDebug("Voice stopped for session: {sessionId}, silence duration: {stopDuration}ms", session.SessionId, stopDuration);
#endif
                            vadStatus.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                            vadStatus.VoiceStop = true;
                            return Task.FromResult(true);
                        }
                    }

                    if (clientHaveVoice)
                    {
                        vadStatus.HaveVoice = true;
                        vadStatus.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }
                }

                return Task.FromResult(clientHaveVoice);
            }
            catch (OperationCanceledException)
            {
                vadStatus.Reset();
                this._modelState.Reset();
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType} in session: {sessionId}", this.ProviderType, session.SessionId);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Resets the ONNX model state for this instance.
        /// Detection state is managed by Session.VadStatusContext.Reset().
        /// </summary>
        public void ResetModelState()
        {
            //todo
            this._modelState?.Reset();
            this.Logger.LogDebug("Reset VAD model state for session: {sessionId}", this.SessionId);
        }

        public override void Dispose()
        {
            this._modelState = null;
        }
    }
}
