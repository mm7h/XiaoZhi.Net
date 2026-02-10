using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.VAD.Sherpa
{
    internal abstract class BaseSherpaVad<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private VoiceActivityDetector? _vad;
        private int _sampleRate = 16000;
        private int _closeConnectionNoVoiceTime = 120;

        private const int SAMPLING_RATE_8K = 8000;
        private const int SAMPLING_RATE_16K = 16000;

        private readonly SemaphoreSlim _vadConvertSlim;
        private readonly ConcurrentDictionary<string, (VadSessionState, IVadEventCallback)> _vadSessions;

        protected BaseSherpaVad(ILogger<TLogger> logger) : base(logger)
        {
            this._vadConvertSlim = new SemaphoreSlim(1, 1);
            this._vadSessions = new ConcurrentDictionary<string, (VadSessionState, IVadEventCallback)>();
        }

        public override string ProviderType => "vad";
        public int FrameSize { get; private set; }

        public bool Build(VadModelConfig vadModelConfig, ModelSetting modelSetting)
        {
            this._sampleRate = modelSetting.Config.GetConfigValueOrDefault("SampleRate", 16000);

            if (this._sampleRate != SAMPLING_RATE_8K && this._sampleRate != SAMPLING_RATE_16K)
            {
                this.Logger.LogError(Lang.BaseSherpaVad_Build_UnsupportedSampleRate, this._sampleRate);
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
            this._vadSessions.AddOrUpdate(deviceId, (vadState, callback), (_, _) => (vadState, callback));
            this.Logger.LogDebug(Lang.BaseSherpaVad_RegisterDevice_Registered, deviceId, sessionId);
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            if (this._vadSessions.TryRemove(deviceId, out _))
            {
                this.Logger.LogDebug(Lang.BaseSherpaVad_UnregisterDevice_Unregistered, deviceId, sessionId);
            }
        }

        public void ResetSessionState(string deviceId, string sessionId)
        {
            if (this._vadSessions.TryGetValue(deviceId, out var context))
            {
                var (state, _) = context;
                state.Reset();
                this.Logger.LogDebug(Lang.BaseSherpaVad_ResetSessionState_Reset, deviceId, sessionId);
            }
        }

        public override bool CheckDeviceRegistered(string deviceId, string sessionId)
        {
            return this._vadSessions.ContainsKey(deviceId);
        }

        public async Task AnalysisVoiceAsync(string deviceId, string sessionId, float[] audioData, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(deviceId, sessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._vad is null)
            {
                throw new ArgumentNullException(Lang.BaseSherpaVad_AnalysisVoiceAsync_VadNotBuilt);
            }

            if (!this._vadSessions.TryGetValue(deviceId, out var context))
            {
                throw new InvalidOperationException(string.Format(Lang.BaseSherpaVad_AnalysisVoiceAsync_SessionStateNotFound, deviceId, sessionId));
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
                            this.Logger.LogDebug(Lang.BaseSherpaVad_AnalysisVoiceAsync_VoiceStopped, deviceId);

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
                this.Logger.LogWarning(Lang.BaseSherpaVad_AnalysisVoiceAsync_UserCanceled, this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.BaseSherpaVad_AnalysisVoiceAsync_UnexpectedError, this.ProviderType);
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
            if (this._vadSessions.TryGetValue(deviceId, out var context))
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
                    this.Logger.LogDebug(Lang.BaseSherpaVad_CheckLongTermSilence_Detected, deviceId, silenceDuration);
                    callback.OnLongTermSilence();
                }
            }
        }

        public override void Dispose()
        {
            this._vadSessions.Clear();
            this._vadConvertSlim.Dispose();
            this._vad?.Clear();
            this._vad?.Dispose();
        }
    }
}
