using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class TeleSpeechCtc : SherpaOfflineAsr<TeleSpeechCtc>
    {
        public TeleSpeechCtc(IAudioEditor audioEditor, ILogger<TeleSpeechCtc> logger) : base(audioEditor, logger)
        {
        }

        public override string ModelName => nameof(TeleSpeechCtc);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("model.onnx", "tokens.txt"))
                {
                    return false;
                }
                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.TeleSpeechCtc = Path.Combine(this.ModelFileFoler, "model.onnx");
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
