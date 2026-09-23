using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class OnlineZipformer2Ctc : SherpaOnlineAsr<OnlineZipformer2Ctc>
    {
        public OnlineZipformer2Ctc(ILogger<OnlineZipformer2Ctc> logger) : base(logger) { }
        public override string ModelName => nameof(OnlineZipformer2Ctc);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("model.onnx", "tokens.txt"))
                {
                    return false;
                }

                OnlineRecognizerConfig config = new OnlineRecognizerConfig();
                config.ModelConfig.Zipformer2Ctc.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
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
