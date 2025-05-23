using Serilog;
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

        private readonly SemaphoreSlim _vadConvertSlim = new SemaphoreSlim(1, 1);

        public Silero(XiaoZhiConfig config, ILogger logger) : base(config.VadSetting, logger)
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
                this.Logger.Information($"Builded the {this.ProviderType} model: {this.ModelName}");
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Invalid model settings for {this.ProviderType}: {ModelName}");
                this.Logger.Error($"Invalid model settings for {this.ProviderType}: {ModelName}");
                return false;
            }

        }

        public async Task<bool> AnalysisVoiceAsync( Session sessionContext, CancellationToken token)
        {
            if (this._vad == null || !this._sampleRate.HasValue || !this._silenceThresholdMs.HasValue)
            {
                throw new ArgumentNullException("Please build vad provider first.");
            }
            try
            {
                await this._vadConvertSlim.WaitAsync(token);

                this._vad.Clear();

                bool client_have_voice = false;

                while (sessionContext.AudioPacketContext.VadPacket.GetFrames(this.FrameSize, out float[] chunk))
                {
                    token.ThrowIfCancellationRequested();
                    if (chunk.Length == 0)
                    {
                        continue;
                    }
                    this._vad.AcceptWaveform(chunk);
                    if (this._vad.IsSpeechDetected() && !this._vad.IsEmpty())
                    {
                        client_have_voice = true;
                    }
                    else
                    {
                        client_have_voice = false;
                    }
                    this._vad.Flush();
                    if (!this._vad.IsEmpty())
                    {
                        client_have_voice = true;
                    }
                    else
                    {
                        client_have_voice = false;
                    }

                    if (sessionContext.VadStatusContext.HaveVoice && !client_have_voice)
                    {
                        long stopDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - sessionContext.VadStatusContext.HaveVoiceLatestTime;
                        if (stopDuration > this._silenceThresholdMs)
                        {
                            this.Logger.Debug("The voice is stopped, let's start the ASR.");
                            sessionContext.VadStatusContext.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                            sessionContext.VadStatusContext.VoiceStop = true;
                            return true;
                        }
                    }

                    if (client_have_voice)
                    {
                        sessionContext.VadStatusContext.HaveVoice = true;
                        sessionContext.VadStatusContext.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }
                }

                this._vad.Flush();

                return client_have_voice;
            }
            catch (OperationCanceledException ex)
            {
                sessionContext.VadStatusContext.Reset();
                this.Logger.Warning($"User canceled the job for {this.ProviderType}.");
                throw ex;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Unexpected error(s): {ex.Message}.");
                this.Logger.Error($"Unexpected error(s) for {this.ProviderType}.");
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
