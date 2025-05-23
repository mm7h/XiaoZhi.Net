using Serilog;
using SherpaOnnx;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal sealed class Kokoro : BaseProvider, ITts
    {
        private OfflineTts? _offlineTts;
        private const float SPEAK_SPPED = 1.0f;
        private const int SPERAKER_ID = 50;

        private readonly SemaphoreSlim _ttsConvertSlim = new SemaphoreSlim(1, 1);

        private bool _save2File = false;
        private string? _savePath;

        public event Action<string, OutSegment> OnBeforeProcessing;
        public event Action<string, float[]> OnProcessing;
        public event Action<string, OutSegment, int> OnProcessed;

        public Kokoro(XiaoZhiConfig config, ILogger logger) : base(config.TtsSetting, logger)
        {
        }

        public override string ProviderType => "tts";

        public int GetTtsSampleRate()
        {
            return this._offlineTts?.SampleRate ?? 16000;
        }

        public override bool Build()
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

                string lexicons = this.ModelSetting.Config.Lexicons ?? "";
                if (!string.IsNullOrEmpty(lexicons))
                {
                    //$"{Path.Combine(this.ModelFileFoler, "lexicon-us-en.txt")},{Path.Combine(this.ModelFileFoler, "lexicon-zh.txt")}"
                    config.Model.Kokoro.Lexicon = Path.Combine(this.ModelFileFoler, lexicons);
                }

                config.Model.NumThreads = 2;
                config.Model.Provider = "cpu";

                this._save2File = this.ModelSetting.Config.Save2File ?? false;


                if (this._save2File)
                {
                    this._savePath = Environment.CurrentDirectory + (this.ModelSetting.Config.SavePath ?? Path.Combine("data", "tts-cache"));
                    if (!Directory.Exists(this._savePath))
                        Directory.CreateDirectory(this._savePath);
                }
                this._offlineTts = new OfflineTts(config);
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

                using (CodeTimer timer = CodeTimer.Create(false))
                {
                    this.OnBeforeProcessing?.Invoke(workflow.SessionId, workflow.Data);

                    OfflineTtsGeneratedAudio audio = this._offlineTts.GenerateWithCallbackProgress(segment,
                            SPEAK_SPPED,
                            SPERAKER_ID,
                            (IntPtr samples, int n, float progress) =>
                            {
                                if (token.IsCancellationRequested)
                                    return 0;
                                float[] data = new float[n];
                                Marshal.Copy(samples, data, 0, n);
                                this.OnProcessing?.Invoke(workflow.SessionId, data);

                                return 1;
                            });

                    int duration = Math.Max((int)(this.CalculateDuration(audio.SampleRate, audio.NumSamples) * 1000 - (workflow.Data.IsFirst ? 300 + timer.ElapsedMilliseconds : 0)), 0);

                    this.OnProcessed.Invoke(workflow.SessionId, workflow.Data, duration);

                    if (this._save2File)
                    {
                        _ = Task.Run(() =>
                        {
                            string fileName = $"{this.ReplaceMacDelimiters(session.DeviceId)}_{DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString()}.wav";
                            string filePath = Path.Combine(this._savePath, fileName);
                            if (File.Exists(filePath))
                                File.Delete(filePath);
                            bool saved = audio.SaveToWaveFile(filePath);
                            if (saved)
                            {
                                this.Logger.Debug($"Saved tts wave file {fileName} successed, the duration of file is: {this.FormatDuration(duration)}s.");
                            }
                            else
                            {
                                this.Logger.Debug($"Failed to save tts wave file {fileName}.");
                            }
                            audio.Dispose();
                        });
                    }
                    else
                    {
                        audio.Dispose();
                        this.Logger.Debug($"TTS generated success, the duration of the voice is: {this.FormatDuration(duration)}.");
                    }
                }


                await Task.CompletedTask;

            }
            catch (OperationCanceledException ex)
            {
                this.Logger.Warning($"User canceled the job for {this.ProviderType}.");
                throw ex;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Unexpected error(s): {ex.Message}.");
                this.Logger.Error($"Unexpected error(s) for {this.ProviderType}.");
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
        private string FormatDuration(long durationInMillisecond)
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
