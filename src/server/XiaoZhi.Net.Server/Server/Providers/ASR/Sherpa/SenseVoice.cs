using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal class SenseVoice : BaseSherpaAsr<SenseVoice>, IAsr
    {
        
        public SenseVoice(XiaoZhiConfig config, ILogger<SenseVoice> logger) : base(logger)
        {
        }
        public override string ModelName => nameof(SenseVoice);
        public override string ProviderType => "asr";

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!CheckModelExist())
                {
                    return false;
                }
                OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
                offlineRecognizerConfig.ModelConfig.SenseVoice.Model = Path.Combine(ModelFileFoler, "model.onnx");
                offlineRecognizerConfig.ModelConfig.SenseVoice.UseInverseTextNormalization = modelSetting.Config.UseInverseTextNormalization ?? 1;
                offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(ModelFileFoler, "tokens.txt");
                offlineRecognizerConfig.DecodingMethod = modelSetting.Config.DecodingMethod ?? "greedy_search";
                if (offlineRecognizerConfig.DecodingMethod == "modified_beam_search")
                {
                    offlineRecognizerConfig.MaxActivePaths = modelSetting.Config.MaxActivePaths ?? 4;
                }
                if (!string.IsNullOrEmpty(modelSetting.Config.HotwordsFile))
                {
                    offlineRecognizerConfig.HotwordsFile = Path.Combine(ModelFileFoler, "hotwords.txt");
                    offlineRecognizerConfig.HotwordsScore = modelSetting.Config.HotwordsScore ?? 1.5F;
                }
                //this._config.RuleFsts = this.ModelSetting.Config.RuleFsts;

                this.BuildOfflineRecognizer(offlineRecognizerConfig);
                Logger.LogInformation("Builded the {providerType} model: {modelName}", ProviderType, ModelName);

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", ProviderType, ModelName);
                return false;
            }
        }

    }
}
