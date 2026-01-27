using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.VAD.Sherpa
{
    internal abstract class BaseSherpaVad<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private VoiceActivityDetector? _vad;
        private int? _sampleRate;
        private int? _silenceThresholdMs;

        private const int REQUIRED_VOICE_FRAMES = 3;
        private const int SAMPLING_RATE_8K = 8000;
        private const int SAMPLING_RATE_16K = 16000;

        private readonly SemaphoreSlim _vadConvertSlim = new SemaphoreSlim(1, 1);

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

            vadModelConfig.SampleRate = this._sampleRate.Value;
            this._silenceThresholdMs = modelSetting.Config.GetConfigValueOrDefault("SilenceThresholdMs", 700);
            this.FrameSize = this._sampleRate == SAMPLING_RATE_16K ? 512 : 256;
            this._vad = new VoiceActivityDetector(vadModelConfig, 60);

            return true;
        }

        public async Task<bool> AnalysisVoiceAsync(Session session, CancellationToken token)
        {
            if (this._vad == null || !this._sampleRate.HasValue || !this._silenceThresholdMs.HasValue)
            {
                throw new ArgumentNullException("Please build vad provider first.");
            }
            try
            {
                //todo: 暂无法满足多session情况下并行使用同一模型
                await this._vadConvertSlim.WaitAsync(token);

                this._vad.Reset();

                bool clientHaveVoice = false;
                int voiceFrameCount = 0;

                while (session.AudioPacketContext.VadPacket.GetFrames(this.FrameSize, out float[] chunk))
                {
                    token.ThrowIfCancellationRequested();
                    if (chunk.Length == 0)
                    {
                        continue;
                    }
                    this._vad.AcceptWaveform(chunk);
                    bool is_voice = this._vad.IsSpeechDetected();
                    if (is_voice)
                    {
                        voiceFrameCount++;
                    }
                    else
                    {
                        voiceFrameCount = 0;
                    }

                    clientHaveVoice = voiceFrameCount >= REQUIRED_VOICE_FRAMES;

                    if (session.VadStatusContext.HaveVoice && !clientHaveVoice)
                    {
                        long stopDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - session.VadStatusContext.HaveVoiceLatestTime;
                        if (stopDuration > this._silenceThresholdMs)
                        {
#if DEBUG
                            this.Logger.LogDebug("The voice is stopped.");
#endif
                            session.VadStatusContext.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                            session.VadStatusContext.VoiceStop = true;
                            return true;
                        }
                    }

                    if (clientHaveVoice)
                    {
                        session.VadStatusContext.HaveVoice = true;
                        session.VadStatusContext.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }
                }

                this._vad.Flush();

                return clientHaveVoice;
            }
            catch (OperationCanceledException)
            {
                session.VadStatusContext.Reset();
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
                return false;
            }
            finally
            {
                this._vad.Reset();
                this._vadConvertSlim.Release();
            }
        }

        public override void Dispose()
        {
            this._vadConvertSlim.Dispose();
            this._vad?.Clear();
            this._vad?.Dispose();
        }
    }
}
