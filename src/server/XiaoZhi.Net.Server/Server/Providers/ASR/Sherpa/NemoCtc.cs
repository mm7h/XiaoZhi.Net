using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class NemoCtc : SherpaOfflineAsr<NemoCtc>
    {
        public NemoCtc(IAudioEditor audioEditor, ILogger<NemoCtc> logger) : base(audioEditor, logger)
        {
        }

        public override string ModelName => nameof(NemoCtc);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("model.onnx", "tokens.txt"))
                {
                    return false;
                }
                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.NeMoCtc.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
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
