using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.VAD
{
    internal class Silero : BaseProvider, IVad
    {

        private VoiceActivityDetector? _vad;
        private int? _sampleRate;
        private int? _silenceThresholdMs;

        private const int REQUIRED_VOICE_FRAMES = 3;

        private readonly SemaphoreSlim _vadConvertSlim = new SemaphoreSlim(1, 1);

        public Silero(XiaoZhiConfig config, ILogger<Silero> logger) : this(config.VadSetting, logger)
        {
        }

        public Silero(ModelSetting vadSetting, ILogger logger) : base(vadSetting, logger)
        {
        }

        public int FrameSize { get; private set; }

        public override string ProviderType => "vad";
        public override bool Build()
        {
            try
            {
                if (!this.CheckModelExist())
                {
                    return false;
                }
                VadModelConfig vadModelConfig = new VadModelConfig();
                vadModelConfig.SileroVad.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
                vadModelConfig.SampleRate = this.ModelSetting.Config.SampleRate;
                this._sampleRate = this.ModelSetting.Config.SampleRate;
                this._silenceThresholdMs = this.ModelSetting.Config.SilenceThresholdMs ?? 700;
                this.FrameSize = vadModelConfig.SileroVad.WindowSize;
                this._vad = new VoiceActivityDetector(vadModelConfig, 60);
                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }

        }

        public async Task<bool> AnalysisVoiceAsync(Session sessionContext, CancellationToken token)
        {
            if (this._vad == null || !this._sampleRate.HasValue || !this._silenceThresholdMs.HasValue)
            {
                throw new ArgumentNullException("Please build vad provider first.");
            }
            try
            {
                await this._vadConvertSlim.WaitAsync(token);

                this._vad.Clear();

                bool clientHaveVoice = false;
                int voiceFrameCount = 0;

                while (sessionContext.AudioPacketContext.VadPacket.GetFrames(this.FrameSize, out float[] chunk))
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

                    if (sessionContext.VadStatusContext.HaveVoice && !clientHaveVoice)
                    {
                        long stopDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - sessionContext.VadStatusContext.HaveVoiceLatestTime;
                        if (stopDuration > this._silenceThresholdMs)
                        {
#if DEBUG
                            this.Logger.LogDebug("The voice is stopped.");
#endif
                            sessionContext.VadStatusContext.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                            sessionContext.VadStatusContext.VoiceStop = true;
                            return true;
                        }
                    }

                    if (clientHaveVoice)
                    {
                        sessionContext.VadStatusContext.HaveVoice = true;
                        sessionContext.VadStatusContext.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }
                }

                this._vad.Flush();

                return clientHaveVoice;
            }
            catch (OperationCanceledException)
            {
                sessionContext.VadStatusContext.Reset();
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
                this._vad.Flush();
                this._vad.Clear();
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
