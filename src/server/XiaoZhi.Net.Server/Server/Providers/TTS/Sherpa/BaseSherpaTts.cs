using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal abstract class BaseSherpaTts<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private OfflineTts? _offlineTts;

        public event Action<OutSegment>? OnBeforeProcessing;
        public event Action<float[]>? OnProcessing;
        public event Action<float[], OutSegment>? OnProcessed;

        protected BaseSherpaTts(ILogger<TLogger> logger) : base(logger)
        {
        }
        public override string ProviderType => "tts";

        public bool Save2File { get; private set; }
        public string SavePath { get; private set; } = string.Empty;
        //https://k2-fsa.github.io/sherpa/onnx/tts/pretrained_models/kokoro.html#map-between-speaker-id-and-speaker-name
        public int SepakerId { get; private set; } = 50;
        public float SpeakSpeed { get; set; } = 1.0f;

        public virtual int GetTtsSampleRate()
        {
            return this._offlineTts?.SampleRate ?? 24000;
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (this._offlineTts == null)
            {
                throw new ArgumentNullException("Please build tts provider first.");
            }

            try
            {
                string segment = workflow.Data.Content;

                Stopwatch timer = Stopwatch.StartNew();
                this.OnBeforeProcessing?.Invoke(workflow.Data);

                OfflineTtsGeneratedAudio audio = this._offlineTts.Generate(segment, this.SpeakSpeed, this.SepakerId);

                double duration = Math.Max((this.CalculateDuration(audio.SampleRate, audio.NumSamples) * 1000 - (workflow.Data.IsFirstSegment ? 300 + timer.ElapsedMilliseconds : 0)), 0);

                this.OnProcessed?.Invoke(audio.Samples, workflow.Data);

                if (this.Save2File)
                {
                    string fileName = $"{this.ReplaceMacDelimiters(workflow.DeviceId)}_{DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString()}.wav";
                    string filePath = Path.Combine(this.SavePath, fileName);
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    bool saved = audio.SaveToWaveFile(filePath);
                    if (saved)
                    {
                        this.Logger.LogDebug("Saved tts wave file {fileName} successed, the duration of file is: {duration}s.", fileName, this.FormatDuration(duration));
                    }
                    else
                    {
                        this.Logger.LogDebug("Failed to save tts wave file {fileName}.", fileName);
                    }
                }
                else
                {
                    this.Logger.LogDebug("TTS generated success, the duration of the voice is: {duration}.", this.FormatDuration(duration));
                }
                audio.Dispose();
                timer.Stop();


                await Task.CompletedTask;

            }
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
            }
        }

        protected void Build(OfflineTtsConfig offlineTtsConfig, ModelSetting modelSetting)
        {
            offlineTtsConfig.Model.NumThreads = 2;
            offlineTtsConfig.Model.Provider = "cpu";

            this.Save2File = modelSetting.Config.Save2File ?? false;
            this.SpeakSpeed = modelSetting.Config.SpeakSpeed ?? 1.0f;
            this.SepakerId = modelSetting.Config.SpeakerId ?? 50;

            if (this.Save2File)
            {
                this.SavePath = modelSetting.Config.SavePath ?? Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
                if (!Directory.Exists(this.SavePath))
                    Directory.CreateDirectory(this.SavePath);
            }
            this._offlineTts = new OfflineTts(offlineTtsConfig);
        }

        private double CalculateDuration(int sampleRate, int numSamples)
        {
            return (double)numSamples / sampleRate;
        }
        private string FormatDuration(double durationInMillisecond)
        {
            int durationInSeconds = (int)durationInMillisecond / 1000;
            int minutes = durationInSeconds / 60;
            int seconds = durationInSeconds % 60;
            return $"{minutes}m {seconds}s";
        }
        public override void Dispose()
        {
            this._offlineTts?.Dispose();
        }
    }
}
