using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class OfflineTransducer : SherpaOfflineAsr<OfflineTransducer>
    {
        public OfflineTransducer(IAudioEditor audioEditor, ILogger<OfflineTransducer> logger) : base(audioEditor, logger)
        {
        }

        public override string ModelName => nameof(OfflineTransducer);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles(
                    "encoder.onnx",
                    "decoder.onnx",
                    "joiner.onnx",
                    "tokens.txt"))
                {
                    return false;
                }
                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.Transducer.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                config.ModelConfig.Transducer.Decoder = Path.Combine(this.ModelFileFoler, "decoder.onnx");
                config.ModelConfig.Transducer.Joiner = Path.Combine(this.ModelFileFoler, "joiner.onnx");
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
