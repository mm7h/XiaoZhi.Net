using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal class Paraformer : BaseSherpaAsr<Paraformer>, IAsr
    {
        public Paraformer(ILogger<Paraformer> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(Paraformer);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelExist())
                {
                    return false;
                }
                OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
                offlineRecognizerConfig.ModelConfig.Paraformer.Model = Path.Combine(ModelFileFoler, "model.onnx");

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
