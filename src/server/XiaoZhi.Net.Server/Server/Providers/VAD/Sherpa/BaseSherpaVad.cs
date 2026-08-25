using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.VAD.Contexts;

namespace XiaoZhi.Net.Server.Providers.VAD.Sherpa
{
    internal abstract class BaseSherpaVad<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private int _closeConnectionNoVoiceTime = 120_000;
        private VadModelConfig? _vadModelConfig;
        private readonly ConcurrentDictionary<string, SherpaVadSessionState> _vadSessions;

        protected BaseSherpaVad(ILogger<TLogger> logger) : base(logger)
        {
            this._vadSessions = new ConcurrentDictionary<string, SherpaVadSessionState>();
        }

        public override string ProviderType => "vad";
        public int FrameSize { get; private set; }

        public bool Build(VadModelConfig vadModelConfig, ModelSetting modelSetting)
        {
            int configuredSampleRate = modelSetting.Config.GetConfigValueOrDefault(
                "SampleRate", GlobalVariables.AudioProcessingSampleRate);
            if (configuredSampleRate != GlobalVariables.AudioProcessingSampleRate)
            {
                this.Logger.LogError(
                    Lang.BaseSherpaVad_Build_NonCanonicalSampleRate,
                    GlobalVariables.AudioProcessingSampleRate, configuredSampleRate);
                return false;
            }

            this._closeConnectionNoVoiceTime = modelSetting.Config.GetConfigValueOrDefault("CloseConnectionNoVoiceTime", 120_000);
            vadModelConfig.SampleRate = GlobalVariables.AudioProcessingSampleRate;
            this._vadModelConfig = vadModelConfig;
            this.FrameSize = 512;
            return true;
        }

        public void RegisterDevice(string deviceId, string sessionId, IVadEventCallback callback)
        {
            if (this._vadModelConfig is null)
            {
                throw new InvalidOperationException(Lang.BaseSherpaVad_AnalysisVoiceAsync_VadNotBuilt);
            }

            var context = new SherpaVadSessionState(new VadSessionState(), callback, new VoiceActivityDetector(this._vadModelConfig.Value, 60));
            if (this._vadSessions.TryGetValue(deviceId, out SherpaVadSessionState? previous))
            {
                previous.Detector.Dispose();
            }
            this._vadSessions[deviceId] = context;
            this.Logger.LogDebug(Lang.BaseSherpaVad_RegisterDevice_Registered, deviceId, sessionId);
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            if (this._vadSessions.TryRemove(deviceId, out SherpaVadSessionState? context))
            {
                context.Detector.Dispose();
                this.Logger.LogDebug(Lang.BaseSherpaVad_UnregisterDevice_Unregistered, deviceId, sessionId);
            }
        }

        public void ResetSessionState(string deviceId, string sessionId)
        {
            if (this._vadSessions.TryGetValue(deviceId, out SherpaVadSessionState? context))
            {
                context.State.Reset();
                context.Detector.Reset();
                this.Logger.LogDebug(Lang.BaseSherpaVad_ResetSessionState_Reset, deviceId, sessionId);
            }
        }

        public override bool CheckDeviceRegistered(string deviceId, string sessionId)
        {
            return this._vadSessions.ContainsKey(deviceId);
        }

        public Task AnalysisVoiceAsync(string deviceId, string sessionId, float[] audioData, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(deviceId, sessionId))
            {
                throw new SessionNotInitializedException();
            }

            if (!this._vadSessions.TryGetValue(deviceId, out SherpaVadSessionState? context))
            {
                throw new InvalidOperationException(string.Format(Lang.BaseSherpaVad_AnalysisVoiceAsync_SessionStateNotFound, deviceId, sessionId));
            }

            VadSessionState vadState = context.State;
            try
            {
                vadState.AppendAudio(audioData);
                while (vadState.TryDequeueFrame(this.FrameSize, out float[] chunk))
                {
                    token.ThrowIfCancellationRequested();
                    vadState.MarkFrameProcessed(chunk.Length);
                    context.Detector.AcceptWaveform(chunk);

                    if (context.Detector.IsSpeechDetected())
                    {
                        bool voiceStarted = !vadState.HaveVoice;
                        vadState.HaveVoice = true;
                        vadState.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                        if (voiceStarted)
                        {
                            context.Callback.OnVoiceStarted();
                        }
                        continue;
                    }

                    if (!context.Detector.IsEmpty())
                    {
                        SpeechSegment speechSegment = context.Detector.Front();
                        this.Logger.LogDebug(Lang.BaseSherpaVad_AnalysisVoiceAsync_VoiceStopped, deviceId);
                        context.Callback.OnVoiceDetected(speechSegment.Samples);
                        vadState.Reset();
                        context.Detector.Reset();
                        return Task.CompletedTask;
                    }
                }

                if (!vadState.HaveVoice && vadState.ProcessedSamplesSinceReset > this.FrameSize * 50)
                {
                    context.Callback.OnVoiceSilence();
                }

                this.CheckLongTermSilence(deviceId, sessionId, context);
            }
            catch (OperationCanceledException)
            {
                vadState.Reset();
                context.Detector.Reset();
                this.Logger.LogWarning(Lang.BaseSherpaVad_AnalysisVoiceAsync_UserCanceled, this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.BaseSherpaVad_AnalysisVoiceAsync_UnexpectedError, this.ProviderType);
            }

            return Task.CompletedTask;
        }

        private void CheckLongTermSilence(string deviceId, string sessionId, SherpaVadSessionState context)
        {
            VadSessionState vadState = context.State;
            if (vadState.HaveVoiceLatestTime == 0)
            {
                vadState.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                return;
            }

            long silenceDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - vadState.HaveVoiceLatestTime;
            if (silenceDuration >= this._closeConnectionNoVoiceTime)
            {
                this.Logger.LogDebug(Lang.BaseSherpaVad_CheckLongTermSilence_Detected, deviceId, silenceDuration);
                context.Callback.OnLongTermSilence();
            }
        }

        public override void Dispose()
        {
            foreach (SherpaVadSessionState context in this._vadSessions.Values)
            {
                context.Detector.Dispose();
            }
            this._vadSessions.Clear();
        }

        private sealed record SherpaVadSessionState(
            VadSessionState State,
            IVadEventCallback Callback,
            VoiceActivityDetector Detector);
    }
}
