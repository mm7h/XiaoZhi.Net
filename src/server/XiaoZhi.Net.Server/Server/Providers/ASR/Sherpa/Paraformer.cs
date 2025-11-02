using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal class Paraformer : BaseSherpaAsr<Paraformer>, IAsr
    {
        private readonly SemaphoreSlim _asrConvertSlim = new SemaphoreSlim(1, 1);
        private OfflineRecognizer? _offlineRecognizer;
        public Paraformer(ILogger<Paraformer> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(Paraformer);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!CheckModelExist())
                {
                    return false;
                }
                OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
                offlineRecognizerConfig.ModelConfig.Paraformer.Model = Path.Combine(ModelFileFoler, "model.onnx");
                offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(ModelFileFoler, "tokens.txt");
                offlineRecognizerConfig.DecodingMethod = modelSetting.Config?.DecodingMethod ?? "greedy_search";
                if (offlineRecognizerConfig.DecodingMethod == "modified_beam_search")
                {
                    offlineRecognizerConfig.MaxActivePaths = modelSetting.Config?.MaxActivePaths ?? 4;
                }
                if (!string.IsNullOrEmpty(modelSetting.Config?.HotwordsFile))
                {
                    offlineRecognizerConfig.HotwordsFile = Path.Combine(ModelFileFoler, "hotwords.txt");
                    offlineRecognizerConfig.HotwordsScore = modelSetting.Config?.HotwordsScore ?? 1.5F;
                }
                //this._config.RuleFsts = this.ModelSetting.Config.RuleFsts;

                _offlineRecognizer = new OfflineRecognizer(offlineRecognizerConfig);
                Logger.LogInformation("Builded the {providerType} model: {modelName}", ProviderType, ModelName);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", ProviderType, ModelName);
                return false;
            }
        }

        public async Task<string> ConvertSpeechText(CircularBuffer voicePackets, int sampleRate, int frameSize, CancellationToken token)
        {
            try
            {
                if (_offlineRecognizer == null)
                {
                    throw new ArgumentNullException("Please build asr provider first.");
                }
                await _asrConvertSlim.WaitAsync(token);

                if (voicePackets.Size > 50)
                {
                    using (var stream = _offlineRecognizer.CreateStream())
                    {
                        while (voicePackets.GetFrames(frameSize, out float[] chunk))
                        {
                            stream.AcceptWaveform(sampleRate, chunk);
                        }

                        _offlineRecognizer.Decode(stream);

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
                Logger.LogWarning("User canceled the job for {providerType}.", ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error(s) for {providerType}.", ProviderType);
                return string.Empty;
            }
            finally
            {
                voicePackets.Reset();
                _asrConvertSlim.Release();
            }
        }


        public override void Dispose()
        {
            _asrConvertSlim.Dispose();
            _offlineRecognizer?.Dispose();
        }
    }
}
