using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class CohereTranscribe : SherpaOfflineAsr<CohereTranscribe>
    {
        public CohereTranscribe(IAudioEditor audioEditor, ILogger<CohereTranscribe> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(CohereTranscribe);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("encoder.onnx", "decoder.onnx", "tokens.txt"))
                {
                    return false;
                }

                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.CohereTranscribe.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                config.ModelConfig.CohereTranscribe.Decoder = Path.Combine(this.ModelFileFoler, "decoder.onnx");
                config.ModelConfig.CohereTranscribe.Language = modelSetting.Config.GetConfigValueOrDefault("Language", "");
                config.ModelConfig.CohereTranscribe.UsePunct = modelSetting.Config.GetConfigValueOrDefault("UsePunctuation", 1);
                config.ModelConfig.CohereTranscribe.UseItn = modelSetting.Config.GetConfigValueOrDefault("UseInverseTextNormalization", 1);
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
