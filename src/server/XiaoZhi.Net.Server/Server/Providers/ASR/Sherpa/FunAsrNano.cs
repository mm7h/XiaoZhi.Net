using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class FunAsrNano : SherpaOfflineAsr<FunAsrNano>
    {
        public FunAsrNano(IAudioEditor audioEditor, ILogger<FunAsrNano> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(FunAsrNano);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("encoder-adaptor.onnx", "llm.onnx", "embedding.onnx") || !this.CheckModelDirectories("tokenizer"))
                {
                    return false;
                }

                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.FunAsrNano.EncoderAdaptor = Path.Combine(this.ModelFileFoler, "encoder-adaptor.onnx");
                config.ModelConfig.FunAsrNano.LLM = Path.Combine(this.ModelFileFoler, "llm.onnx");
                config.ModelConfig.FunAsrNano.Embedding = Path.Combine(this.ModelFileFoler, "embedding.onnx");
                config.ModelConfig.FunAsrNano.Tokenizer = Path.Combine(this.ModelFileFoler, "tokenizer");
                config.ModelConfig.FunAsrNano.SystemPrompt = modelSetting.Config.GetConfigValueOrDefault("SystemPrompt", "You are a helpful assistant.");
                config.ModelConfig.FunAsrNano.UserPrompt = modelSetting.Config.GetConfigValueOrDefault("UserPrompt", "语音转写：");
                config.ModelConfig.FunAsrNano.MaxNewTokens = modelSetting.Config.GetConfigValueOrDefault("MaxNewTokens", 512);
                config.ModelConfig.FunAsrNano.Temperature = modelSetting.Config.GetConfigValueOrDefault("Temperature", 1e-6F);
                config.ModelConfig.FunAsrNano.TopP = modelSetting.Config.GetConfigValueOrDefault("TopP", 0.8F);
                config.ModelConfig.FunAsrNano.Seed = modelSetting.Config.GetConfigValueOrDefault("Seed", 42);
                config.ModelConfig.FunAsrNano.Language = modelSetting.Config.GetConfigValueOrDefault("Language", "");
                config.ModelConfig.FunAsrNano.Itn = modelSetting.Config.GetConfigValueOrDefault("UseInverseTextNormalization", 0);
                config.ModelConfig.FunAsrNano.Hotwords = modelSetting.Config.GetConfigValueOrDefault("Hotwords", "");
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
