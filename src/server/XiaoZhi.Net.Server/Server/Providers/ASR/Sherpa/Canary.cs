using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class Canary : SherpaOfflineAsr<Canary>
    {
        public Canary(IAudioEditor audioEditor, ILogger<Canary> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(Canary);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("encoder.onnx", "decoder.onnx", "tokens.txt"))
                {
                    return false;
                }

                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.Canary.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                config.ModelConfig.Canary.Decoder = Path.Combine(this.ModelFileFoler, "decoder.onnx");
                config.ModelConfig.Canary.SrcLang = modelSetting.Config.GetConfigValueOrDefault("SourceLanguage", "en");
                config.ModelConfig.Canary.TgtLang = modelSetting.Config.GetConfigValueOrDefault("TargetLanguage", "en");
                config.ModelConfig.Canary.UsePnc = modelSetting.Config.GetConfigValueOrDefault("UsePunctuation", 1);
                this.Build(config, modelSetting);
                this.LogBuilt();
                return true;
            }
            catch (Exception ex)
            {
                this.LogBuildError(ex);
                return false;
            }
        }
    }
}
