using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class OnlineParaformer : SherpaOnlineAsr<OnlineParaformer>
    {
        public OnlineParaformer(ILogger<OnlineParaformer> logger) : base(logger) { }
        public override string ModelName => nameof(OnlineParaformer);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("encoder.onnx", "decoder.onnx", "tokens.txt"))
                {
                    return false;
                }

                OnlineRecognizerConfig config = new OnlineRecognizerConfig();
                config.ModelConfig.Paraformer.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                config.ModelConfig.Paraformer.Decoder = Path.Combine(this.ModelFileFoler, "decoder.onnx");
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
