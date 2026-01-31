using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Resources.OnnxModels;
using XiaoZhi.Net.Server.Resources.OnnxModels.VAD;
using XiaoZhi.Net.Server.Resources.OnnxModels.VAD.Models;

namespace XiaoZhi.Net.Server.Providers.VAD.Native
{
    /// <summary>
    /// Silero VAD v4 implementation
    /// </summary>
    internal sealed class SileroNative : BaseProvider<SileroNative, ModelSetting>, IVad
    {
        private readonly IServiceProvider _serviceProvider;

        private IVadOnnxModel? _vadOnnxModel;
        private int _sampleRate = 16000;
        private int _closeConnectionNoVoiceTime = 120;

        private float _silenceThresholdSecond;
        private float _threshold;
        private float _thresholdLow;

        private const int FRAME_WINDOW_THRESHOLD = 5;
        private const int SAMPLING_RATE_8K = 8000;
        private const int SAMPLING_RATE_16K = 16000;

        private SileroModelState? _sileroModelState;
        private VadSessionState? _vadSessionState;

        private IVadEventCallback? _vadEventCallback;

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

                if (this._sampleRate != SAMPLING_RATE_8K && this._sampleRate != SAMPLING_RATE_16K)
                {
                    this.Logger.LogError(Lang.SileroNative_Build_UnsupportedSampleRate, this._sampleRate);
                    return false;
                }

                this._silenceThresholdSecond = modelSetting.Config.GetConfigValueOrDefault("SilenceThresholdSecond", 0.7f);
                this._threshold = modelSetting.Config.GetConfigValueOrDefault("Threshold", 0.5f);
                this._thresholdLow = modelSetting.Config.GetConfigValueOrDefault("ThresholdLow", 0.2f);
                this._closeConnectionNoVoiceTime = modelSetting.Config.GetConfigValueOrDefault("CloseConnectionNoVoiceTime", 120);

                this.FrameSize = this._sampleRate == SAMPLING_RATE_16K ? 512 : 256;

                this._vadOnnxModel = this._serviceProvider.GetRequiredService<IVadOnnxModel>();

                this._sileroModelState = SileroOnnx.CreateModelState(this._sampleRate);

                this.Logger.LogInformation(Lang.SileroNative_Build_Built, this.ProviderType, this.ModelName);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.SileroNative_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }
        }
        public void RegisterDevice(string deviceId, string sessionId, IVadEventCallback callback)
        {
            this._vadEventCallback = callback;
            this._vadSessionState = new VadSessionState();

            this.RegisterDevice(deviceId, sessionId);
        }

        public void ResetSessionState(string deviceId, string sessionId)
        {
            this._sileroModelState?.Reset();
            this._vadSessionState?.Reset();
        }

        public Task AnalysisVoiceAsync(string deviceId, string sessionId, float[] audioData, CancellationToken token)
        {
            if (this._vadOnnxModel is null)
            {
                throw new ArgumentNullException(Lang.SileroNative_AnalysisVoiceAsync_ProviderNotBuilt);
            }

            if (this._sileroModelState is null || this._vadSessionState is null)
            {
                throw new ArgumentNullException(Lang.SileroNative_AnalysisVoiceAsync_ProviderNotBuilt);
            }

            try
            {
                int analyzedIndex = this._vadSessionState.AnalyzedIndex;

                while (audioData.GetSlidingFrame(this.FrameSize, ref analyzedIndex, out float[] chunk))
                {
                    token.ThrowIfCancellationRequested();

                    if (chunk.Length == 0)
                    {
                        continue;
                    }

                    float speechProb = this._vadOnnxModel.Infer(chunk, this._sampleRate, this._sileroModelState);

                    bool isSpeechDetected;
                    if (speechProb >= this._threshold)
                    {
                        isSpeechDetected = true;
                    }
                    else if (speechProb <= this._thresholdLow)
                    {
                        isSpeechDetected = false;
                    }
                    else
                    {
                        isSpeechDetected = this._vadSessionState.LastIsVoice;
                    }

                    this._vadSessionState.LastIsVoice = isSpeechDetected;

                    this._vadSessionState.AddToVoiceWindow(isSpeechDetected);

                    bool clientHaveVoice = this._vadSessionState.CountVoiceInWindow() >= FRAME_WINDOW_THRESHOLD;

                    if (this._vadSessionState.HaveVoice && !clientHaveVoice)
                    {
                        long stopDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - this._vadSessionState.HaveVoiceLatestTime;
                        if (stopDuration >= this._silenceThresholdSecond * 1000)
                        {
                            this.Logger.LogDebug(Lang.SileroNative_AnalysisVoiceAsync_VoiceStopped, deviceId, stopDuration);
                            this._vadSessionState.VoiceStop = true;

                            this._vadEventCallback?.OnVoiceDetected(audioData);
                            this._vadSessionState.Reset();
                            return Task.CompletedTask;
                        }
                    }

                    if (clientHaveVoice && !this._vadSessionState.HaveVoice)
                    {
                        this._vadSessionState.HaveVoice = true;
                        this._vadSessionState.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }
                }

                if (!this._vadSessionState.HaveVoice && analyzedIndex > this.FrameSize * 50)
                {
                    this._vadEventCallback?.OnVoiceSilence();
                }

                this.CheckLongTermSilence(deviceId, sessionId, this._vadSessionState);

                return Task.CompletedTask;
            }
            catch (OperationCanceledException)
            {
                this._sileroModelState.Reset();
                this._vadSessionState.Reset();
                this.Logger.LogWarning(Lang.SileroNative_AnalysisVoiceAsync_UserCanceled, this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.SileroNative_AnalysisVoiceAsync_UnexpectedError, this.ProviderType, deviceId);
                return Task.CompletedTask;
            }
            finally
            {
                this._vadSessionState.AnalyzedIndex = 0;
            }
        }

        private void CheckLongTermSilence(string deviceId, string sessionId, VadSessionState vadState)
        {
            if (vadState.HaveVoiceLatestTime == 0)
            {
                vadState.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                return;
            }

            long silenceDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - vadState.HaveVoiceLatestTime;
            long longTermSilenceThresholdMs = this._closeConnectionNoVoiceTime * 1000;

            if (silenceDuration >= longTermSilenceThresholdMs)
            {
                this.Logger.LogDebug(Lang.SileroNative_CheckLongTermSilence_Detected, deviceId, silenceDuration);
                this._vadEventCallback?.OnLongTermSilence();
            }
        }

        public override void Dispose()
        {
            this._sileroModelState?.Reset();
            this._vadSessionState?.Reset();
        }
    }
}
