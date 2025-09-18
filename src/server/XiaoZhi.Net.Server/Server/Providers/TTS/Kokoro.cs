using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal sealed class Kokoro : BaseProvider<Kokoro, ModelSetting>, ITts
    {
        private OfflineTts? _offlineTts;
        private const float SPEAK_SPPED = 1.0f;
        //https://k2-fsa.github.io/sherpa/onnx/tts/pretrained_models/kokoro.html#map-between-speaker-id-and-speaker-name
        private const int SPERAKER_ID = 50;

        private readonly SemaphoreSlim _ttsConvertSlim = new SemaphoreSlim(1, 1);

        private bool _save2File = false;
        private string? _savePath;

        public event Action<string, OutSegment>? OnBeforeProcessing;
        public event Action<string, float[]>? OnProcessing;
        public event Action<string, float[], OutSegment, double>? OnProcessed;

        public Kokoro(ILogger<Kokoro> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(Kokoro);
        public override string ProviderType => "tts";

        public int GetTtsSampleRate()
        {
            return this._offlineTts?.SampleRate ?? 24000;
        }

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelExist())
                {
                    return false;
                }
                var config = new OfflineTtsConfig();
                config.Model.Kokoro.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
                config.Model.Kokoro.Voices = Path.Combine(this.ModelFileFoler, "voices.bin");
                config.Model.Kokoro.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
                config.Model.Kokoro.DataDir = Path.Combine(this.ModelFileFoler, "espeak-ng-data");
                config.Model.Kokoro.DictDir = Path.Combine(this.ModelFileFoler, "dict");

                string lexicons = modelSetting.Config.Lexicons ?? "";
                if (!string.IsNullOrEmpty(lexicons))
                {
                    string lexiconPath = string.Join(',', lexicons.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(l => Path.Combine(this.ModelFileFoler, l)));
                    config.Model.Kokoro.Lexicon = lexiconPath;
                }

                config.Model.NumThreads = 2;
                config.Model.Provider = "cpu";

                this._save2File = modelSetting.Config.Save2File ?? false;


                if (this._save2File)
                {
                    this._savePath = modelSetting.Config.SavePath ?? Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
                    if (!Directory.Exists(this._savePath))
                        Directory.CreateDirectory(this._savePath);
                }
                this._offlineTts = new OfflineTts(config);
                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, Session session, CancellationToken token)
        {
            if (this._offlineTts == null)
            {
                throw new ArgumentNullException("Please build tts provider first.");
            }

            try
            {
                await this._ttsConvertSlim.WaitAsync(token);
                string segment = workflow.Data.Content;

                Stopwatch timer = Stopwatch.StartNew();
                this.OnBeforeProcessing?.Invoke(workflow.SessionId, workflow.Data);

                OfflineTtsGeneratedAudio audio = this._offlineTts.Generate(segment, SPEAK_SPPED, SPERAKER_ID);

                double duration = Math.Max((this.CalculateDuration(audio.SampleRate, audio.NumSamples) * 1000 - (workflow.Data.IsFirstSegment ? 300 + timer.ElapsedMilliseconds : 0)), 0);

                this.OnProcessed?.Invoke(workflow.SessionId, audio.Samples, workflow.Data, duration);

                if (this._save2File)
                {
                    await Task.Run(() =>
                    {
                        string fileName = $"{this.ReplaceMacDelimiters(session.DeviceId)}_{DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString()}.wav";
                        string filePath = Path.Combine(this._savePath!, fileName);
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
                        audio.Dispose();
                    }).ConfigureAwait(false);
                }
                else
                {
                    audio.Dispose();
                    this.Logger.LogDebug("TTS generated success, the duration of the voice is: {duration}.", this.FormatDuration(duration));
                }
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
            finally
            {
                this._ttsConvertSlim.Release();
            }
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
