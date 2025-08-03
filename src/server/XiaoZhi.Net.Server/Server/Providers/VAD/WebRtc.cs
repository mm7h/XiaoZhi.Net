//The lib from https://github.com/ladenedge/WebRtcVadSharp
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WebRtcVadSharp;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.VAD
{
    internal class WebRtc : BaseProvider, IVad
    {
        private WebRtcVad? _vad;
        private SampleRate _sampleRate;
        private int? _silenceThresholdMs;

        private readonly SemaphoreSlim _vadConvertSlim = new SemaphoreSlim(1, 1);
        public WebRtc(XiaoZhiConfig config, ILogger<WebRtc> logger) : this(config.VadSetting, logger)
        {
        }
        public WebRtc(ModelSetting vadSetting, ILogger logger) : base(vadSetting, logger)
        {
        }
        public override string ProviderType => "vad";
        public int FrameSize => throw new NotImplementedException();
        public override bool Build()
        {
            try
            {
                string libPath = this.ModelSetting.Config?.LibPath ?? "";

                if (!File.Exists(libPath))
                {
                    this.Logger.LogError("Cannot found the lib file in path: {libPath}.", libPath);
                    return false;
                }

                int sampleRate = this.ModelSetting.Config?.SampleRate ?? 16000;
                this._silenceThresholdMs = this.ModelSetting.Config?.SilenceThresholdMs ?? 700;
                IntPtr _dllHandle = WebRtc.LoadLibrary(libPath);

                if (_dllHandle == IntPtr.Zero)
                {
                    this.Logger.LogError("Invalid model settings for {providerType}: {modelName}, failed to load DLL: {libPath}", this.ProviderType, this.ModelName, libPath);
                    return false;
                }

                switch (sampleRate)
                {
                    case 8000:
                        this._sampleRate = SampleRate.Is8kHz;
                        break;
                    case 32000:
                        this._sampleRate = SampleRate.Is32kHz;
                        break;
                    case 48000:
                        this._sampleRate = SampleRate.Is48kHz;
                        break;
                    case 16000:
                    default:
                        this._sampleRate = SampleRate.Is16kHz;
                        break;
                }

                this._vad = new WebRtcVad();
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
            if (this._vad == null)
            {
                throw new ArgumentNullException("Please build vad provider first.");
            }
            try
            {
                await this._vadConvertSlim.WaitAsync(token);
                bool client_have_voice = false;
                while (sessionContext.AudioPacketContext.VadPacket.GetFrames(this.FrameSize, out float[] chunk))
                {
                    token.ThrowIfCancellationRequested();
                    byte[] chunkBytes = chunk.Float2PcmBytes();
                    client_have_voice = this._vad.HasSpeech(chunkBytes, this._sampleRate, FrameLength.Is30ms);

                    if (sessionContext.VadStatusContext.HaveVoice && !client_have_voice)
                    {
                        long stopDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - sessionContext.VadStatusContext.HaveVoiceLatestTime;
                        if (stopDuration > this._silenceThresholdMs)
                        {
# if DEBUG
                            this.Logger.LogDebug("The voice is stopped, let's start the ASR.");
#endif
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

                return client_have_voice;
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
                this._vadConvertSlim.Release();
            }
        }

        public override void Dispose()
        {
            this._vad?.Dispose();
            this._vadConvertSlim.Dispose();
        }

        private static IntPtr LoadLibrary(string lpFileName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return LoadWindowsLibrary(lpFileName);
            }
            else
            {
                return LoadPosixLibrary(lpFileName);
            }
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadWindowsLibrary(string lpFileName);

        [DllImport("libdl", SetLastError = true)]
        private static extern IntPtr LoadPosixLibrary(string lpFileName);
    }
}
