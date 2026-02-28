using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal class Paraformer : BaseSherpaAsr<Paraformer>, IAsr
    {
        public Paraformer(IAudioEditor audioEditor, ILogger<Paraformer> logger) : base(audioEditor, logger)
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

                this.Logger.LogInformation(Lang.Paraformer_Build_Built, this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.Paraformer_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }
        }
    }
}
