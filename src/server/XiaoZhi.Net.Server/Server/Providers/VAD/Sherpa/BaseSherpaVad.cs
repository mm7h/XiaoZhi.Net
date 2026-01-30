using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.VAD.Sherpa
{
    internal abstract class BaseSherpaVad<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private VoiceActivityDetector? _vad;
        private int _sampleRate = 16000;
        private int _closeConnectionNoVoiceTime = 120;

        private const int SAMPLING_RATE_8K = 8000;
        private const int SAMPLING_RATE_16K = 16000;

        private readonly SemaphoreSlim _vadConvertSlim = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, (VadSessionState, IVadEventCallback)> _sessionStates = new();

        protected BaseSherpaVad(ILogger<TLogger> logger) : base(logger)
        {
        }

        public override string ProviderType => "vad";
        public int FrameSize { get; private set; }

        public bool Build(VadModelConfig vadModelConfig, ModelSetting modelSetting)
        {
            this._sampleRate = modelSetting.Config.GetConfigValueOrDefault("SampleRate", 16000);

            if (this._sampleRate != SAMPLING_RATE_8K && this._sampleRate != SAMPLING_RATE_16K)
            {
                this.Logger.LogError("Unsupported sample rate: {sampleRate}. Only 8000 and 16000 are supported.", this._sampleRate);
                return false;
            }

            this._closeConnectionNoVoiceTime = modelSetting.Config.GetConfigValueOrDefault("CloseConnectionNoVoiceTime", 120);

            vadModelConfig.SampleRate = this._sampleRate;
            this.FrameSize = this._sampleRate == SAMPLING_RATE_16K ? 512 : 256;
            this._vad = new VoiceActivityDetector(vadModelConfig, 60);

            return true;
        }

        public void RegisterDevice(string deviceId, string sessionId, IVadEventCallback callback)
        {
            VadSessionState vadState = new VadSessionState();
            this._sessionStates.AddOrUpdate(deviceId, (vadState, callback), (_, _) => (vadState, callback));
            this.Logger.LogDebug("Registered VAD session state for device: {deviceId}, session: {sessionId}", deviceId, sessionId);
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            if (this._sessionStates.TryRemove(deviceId, out _))
            {
                this.Logger.LogDebug("Unregistered VAD session state for device: {deviceId}, session: {sessionId}", deviceId, sessionId);
            }
        }

        public void ResetSessionState(string deviceId, string sessionId)
        {
            if (this._sessionStates.TryGetValue(deviceId, out var context))
            {
                var (state, _) = context;
                state.Reset();
                this.Logger.LogDebug("Reset VAD session state for device: {deviceId}, session: {sessionId}", deviceId, sessionId);
            }
        }

        public async Task AnalysisVoiceAsync(string deviceId, string sessionId, float[] audioData, CancellationToken token)
        {
            if (this._vad is null)
            {
                throw new ArgumentNullException("Please build vad provider first.");
            }

            string key = GetSessionKey(deviceId, sessionId);
            if (!this._sessionStates.TryGetValue(key, out var context))
            {
                throw new InvalidOperationException($"Session state not found for device: {deviceId}, session: {sessionId}. Please register the device first.");
            }
            var (vadState, callback) = context;
            try
            {
                await this._vadConvertSlim.WaitAsync(token);

                this._vad.Reset();

                int analyzedIndex = vadState.AnalyzedIndex;

                while (audioData.GetSlidingFrame(this.FrameSize, ref analyzedIndex, out float[] chunk))
                {
                    token.ThrowIfCancellationRequested();

                    if (chunk.Length == 0)
                    {
                        continue;
                    }

                    this._vad.AcceptWaveform(chunk);

                    bool isSpeaking = this._vad.IsSpeechDetected();

                    if (isSpeaking)
                    {
                        vadState.HaveVoice = true;
                        vadState.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                        continue;
                    }
                    else
                    {
                        if (!this._vad.IsEmpty())
                        {
                            SpeechSegment speechSegment = this._vad.Front();
                            this.Logger.LogDebug("The voice is stopped for device: {deviceId}.", deviceId);

                            callback.OnVoiceDetected(speechSegment.Samples);
                            vadState.Reset();

                            return;
                        }
                    }
                }

                if (!this._vad.IsSpeechDetected() && analyzedIndex > this.FrameSize * 50)
                {
                    callback.OnVoiceSilence();
                }

                this.CheckLongTermSilence(deviceId, sessionId, vadState);
            }
            catch (OperationCanceledException)
            {
                vadState.Reset();
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
            }
            finally
            {
                vadState.AnalyzedIndex = 0;
                this._vad.Reset();
                this._vadConvertSlim.Release();
            }
        }

        private void CheckLongTermSilence(string deviceId, string sessionId, VadSessionState vadState)
        {
            if (this._sessionStates.TryGetValue(deviceId, out var context))
            {
                var (_, callback) = context;
                if (vadState.HaveVoiceLatestTime == 0)
                {
                    vadState.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    return;
                }

                long silenceDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - vadState.HaveVoiceLatestTime;
                long longTermSilenceThresholdMs = this._closeConnectionNoVoiceTime * 1000;

                if (silenceDuration >= longTermSilenceThresholdMs)
                {
                    this.Logger.LogDebug("Long term silence detected for device: {deviceId}, duration: {silenceDuration}ms", deviceId, silenceDuration);
                    callback.OnLongTermSilence();
                }
            }
        }

        private static string GetSessionKey(string deviceId, string sessionId)
        {
            return $"{deviceId}:{sessionId}";
        }

        public override void Dispose()
        {
            this._sessionStates.Clear();
            this._vadConvertSlim.Dispose();
            this._vad?.Clear();
            this._vad?.Dispose();
        }
    }
}
