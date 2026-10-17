using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class OnlineTransducer : SherpaOnlineAsr<OnlineTransducer>
    {
        public OnlineTransducer(ILogger<OnlineTransducer> logger) : base(logger) { }
        public override string ModelName => nameof(OnlineTransducer);

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

                OnlineRecognizerConfig config = new OnlineRecognizerConfig();
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
