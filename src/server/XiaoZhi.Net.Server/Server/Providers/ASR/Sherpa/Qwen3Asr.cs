using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class Qwen3Asr : SherpaOfflineAsr<Qwen3Asr>
    {
        public Qwen3Asr(IAudioEditor audioEditor, ILogger<Qwen3Asr> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(Qwen3Asr);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("conv-frontend.onnx", "encoder.onnx", "decoder.onnx") || !this.CheckModelDirectories("tokenizer"))
                {
                    return false;
                }

                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.Qwen3Asr.ConvFrontend = Path.Combine(this.ModelFileFoler, "conv-frontend.onnx");
                config.ModelConfig.Qwen3Asr.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                config.ModelConfig.Qwen3Asr.Decoder = Path.Combine(this.ModelFileFoler, "decoder.onnx");
                config.ModelConfig.Qwen3Asr.Tokenizer = Path.Combine(this.ModelFileFoler, "tokenizer");
                config.ModelConfig.Qwen3Asr.MaxTotalLen = modelSetting.Config.GetConfigValueOrDefault("MaxTotalLen", 512);
                config.ModelConfig.Qwen3Asr.MaxNewTokens = modelSetting.Config.GetConfigValueOrDefault("MaxNewTokens", 128);
                config.ModelConfig.Qwen3Asr.Temperature = modelSetting.Config.GetConfigValueOrDefault("Temperature", 1e-6F);
                config.ModelConfig.Qwen3Asr.TopP = modelSetting.Config.GetConfigValueOrDefault("TopP", 0.8F);
                config.ModelConfig.Qwen3Asr.Seed = modelSetting.Config.GetConfigValueOrDefault("Seed", 42);
                config.ModelConfig.Qwen3Asr.Hotwords = modelSetting.Config.GetConfigValueOrDefault("Hotwords", "");
                this.Build(config, modelSetting, false);
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
