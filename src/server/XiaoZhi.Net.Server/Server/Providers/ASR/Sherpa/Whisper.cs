using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class Whisper : SherpaOfflineAsr<Whisper>
    {
        public Whisper(IAudioEditor audioEditor, ILogger<Whisper> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(Whisper);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("encoder.onnx", "decoder.onnx", "tokens.txt"))
                {
                    return false;
                }

                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.Whisper.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                config.ModelConfig.Whisper.Decoder = Path.Combine(this.ModelFileFoler, "decoder.onnx");
                config.ModelConfig.Whisper.Language = modelSetting.Config.GetConfigValueOrDefault("Language", "");
                config.ModelConfig.Whisper.Task = modelSetting.Config.GetConfigValueOrDefault("Task", "transcribe");
                config.ModelConfig.Whisper.TailPaddings = modelSetting.Config.GetConfigValueOrDefault("TailPaddings", -1);
                config.ModelConfig.Whisper.EnableTokenTimestamps = modelSetting.Config.GetConfigValueOrDefault("EnableTokenTimestamps", 0);
                config.ModelConfig.Whisper.EnableSegmentTimestamps = modelSetting.Config.GetConfigValueOrDefault("EnableSegmentTimestamps", 0);
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
