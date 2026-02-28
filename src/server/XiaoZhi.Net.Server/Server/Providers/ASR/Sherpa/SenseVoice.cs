using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal class SenseVoice : BaseSherpaAsr<SenseVoice>, IAsr
    {

        public SenseVoice(IAudioEditor audioEditor, ILogger<SenseVoice> logger) : base(audioEditor, logger)
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
                offlineRecognizerConfig.ModelConfig.SenseVoice.UseInverseTextNormalization = modelSetting.Config.GetConfigValueOrDefault("UseInverseTextNormalization", 1);

                this.Build(offlineRecognizerConfig, modelSetting);

                this.Logger.LogInformation(Lang.SenseVoice_Build_Built, this.ProviderType, this.ModelName);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.SenseVoice_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }
        }

    }
}
