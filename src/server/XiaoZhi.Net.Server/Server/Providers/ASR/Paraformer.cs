using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.ASR
{
    internal class Paraformer : BaseProvider, IAsr
    {
        private readonly SemaphoreSlim _asrConvertSlim = new SemaphoreSlim(1, 1);
        private OfflineRecognizer? _offlineRecognizer;
        public Paraformer(XiaoZhiConfig config, ILogger<Paraformer> logger) : this(config.AsrSetting, logger)
        {
        }
        public Paraformer(ModelSetting asrSetting, ILogger logger) : base(asrSetting, logger)
        {
        }
        public override string ProviderType => "asr";

        public override bool Build()
        {
            try
            {
                if (!this.CheckModelExist())
                {
                    return false;
                }
                OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
                offlineRecognizerConfig.ModelConfig.Paraformer.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
                offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
                offlineRecognizerConfig.DecodingMethod = this.ModelSetting.Config?.DecodingMethod ?? "greedy_search";
                if (offlineRecognizerConfig.DecodingMethod == "modified_beam_search")
                {
                    offlineRecognizerConfig.MaxActivePaths = this.ModelSetting.Config?.MaxActivePaths ?? 4;
                }
                if (!string.IsNullOrEmpty(this.ModelSetting.Config?.HotwordsFile))
                {
                    offlineRecognizerConfig.HotwordsFile = Path.Combine(this.ModelFileFoler, "hotwords.txt");
                    offlineRecognizerConfig.HotwordsScore = this.ModelSetting.Config?.HotwordsScore ?? 1.5F;
                }
                //this._config.RuleFsts = this.ModelSetting.Config.RuleFsts;

                this._offlineRecognizer = new OfflineRecognizer(offlineRecognizerConfig);
                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public async Task<string> ConvertSpeechText(CircularBuffer voicePackets, int sampleRate, int frameSize, CancellationToken token)
        {
            try
            {
                if (this._offlineRecognizer == null)
                {
                    throw new ArgumentNullException("Please build asr provider first.");
                }
                await this._asrConvertSlim.WaitAsync(token);

                if (voicePackets.Size > 50)
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
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
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
