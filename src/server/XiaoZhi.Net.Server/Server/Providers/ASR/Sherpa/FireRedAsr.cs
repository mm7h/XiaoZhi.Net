using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class FireRedAsr : SherpaOfflineAsr<FireRedAsr>
    {
        public FireRedAsr(IAudioEditor audioEditor, ILogger<FireRedAsr> logger) : base(audioEditor, logger)
        {
        }

        public override string ModelName => nameof(FireRedAsr);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("encoder.onnx", "decoder.onnx", "tokens.txt"))
                {
                    return false;
                }
                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.FireRedAsr.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                config.ModelConfig.FireRedAsr.Decoder = Path.Combine(this.ModelFileFoler, "decoder.onnx");
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
