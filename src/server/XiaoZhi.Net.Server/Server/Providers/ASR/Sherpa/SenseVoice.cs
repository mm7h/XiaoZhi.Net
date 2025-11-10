using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;

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
                if (!this.CheckModelExist())
                {
                    return false;
                }
                OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
                offlineRecognizerConfig.ModelConfig.SenseVoice.Model = Path.Combine(ModelFileFoler, "model.onnx");
                offlineRecognizerConfig.ModelConfig.SenseVoice.UseInverseTextNormalization = modelSetting.Config.UseInverseTextNormalization ?? 1;

                this.Build(offlineRecognizerConfig, modelSetting);
                
                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

    }
}
