using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class OnlineNemoCtc : SherpaOnlineAsr<OnlineNemoCtc>
    {
        public OnlineNemoCtc(ILogger<OnlineNemoCtc> logger) : base(logger) { }
        public override string ModelName => nameof(OnlineNemoCtc);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("model.onnx", "tokens.txt"))
                {
                    return false;
                }

                OnlineRecognizerConfig config = new OnlineRecognizerConfig();
                config.ModelConfig.NemoCtc.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
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
