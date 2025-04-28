using Serilog;
using SherpaOnnx;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.ASR
{
    internal class SenseVoice : BaseProvider, IAsr
    {
        private readonly SemaphoreSlim _asrConvertSlim = new SemaphoreSlim(1, 1);
        private OfflineRecognizer? _offlineRecognizer;
        public SenseVoice(XiaoZhiConfig config, ILogger logger) : base(config.AsrSetting, logger)
        {
        }

        public override string ProviderType => "asr";

        public override bool Initialize()
        {
            try
            {
                if (!this.CheckModelExist())
                {
                    return false;
                }
                OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
                offlineRecognizerConfig.ModelConfig.SenseVoice.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
                offlineRecognizerConfig.ModelConfig.SenseVoice.UseInverseTextNormalization = this.ModelSetting.Config.UseInverseTextNormalization ?? 1;
                offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
                offlineRecognizerConfig.DecodingMethod = this.ModelSetting.Config.DecodingMethod ?? "greedy_search";
                if (offlineRecognizerConfig.DecodingMethod == "modified_beam_search")
                {
                    offlineRecognizerConfig.MaxActivePaths = this.ModelSetting.Config.MaxActivePaths ?? 4;
                }
                if (!string.IsNullOrEmpty(this.ModelSetting.Config.HotwordsFile))
                {
                    offlineRecognizerConfig.HotwordsFile = Path.Combine(this.ModelFileFoler, "hotwords.txt");
                    offlineRecognizerConfig.HotwordsScore = this.ModelSetting.Config.HotwordsScore ?? 1.5F;
                }
                //this._config.RuleFsts = this.ModelSetting.Config.RuleFsts;

                this._offlineRecognizer = new OfflineRecognizer(offlineRecognizerConfig);
                this.Logger.Information($"Initialized the {this.ProviderType} model: {this.ModelName}");
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Invalid model settings for {this.ProviderType}: {this.ModelName}");
                this.Logger.Error($"Invalid model settings for {this.ProviderType}: {this.ModelName}");
                return false;
            }
        }

        public async Task<string> ConvertSpeechText( CircularBuffer voicePackets, int sampleRate, int frameSize, CancellationToken token)
        {
            try
            {
                if (this._offlineRecognizer == null)
                {
                    throw new ArgumentNullException("Please initialize asr provider first.");
                }
                await this._asrConvertSlim.WaitAsync(token);

                if (voicePackets.Size > 10)
                {
                    using (var stream = this._offlineRecognizer.CreateStream())
                    {
                        while (voicePackets.GetFrames(frameSize, out float[] chunk))
                        {
                            stream.AcceptWaveform(sampleRate, chunk);
                        }

                        this._offlineRecognizer.Decode(stream);

                        string speechResult = stream.Result.Text;
                        return speechResult;
                    }
                }
                else
                {
                    return string.Empty;
                }
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
                return string.Empty;
            }
            finally
            {
                voicePackets.Reset();
                this._asrConvertSlim.Release();
            }


        }


        public override void Dispose()
        {
            this._asrConvertSlim.Dispose();
            this._offlineRecognizer?.Dispose();
        }
    }
}
