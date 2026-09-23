using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal sealed class Moonshine : SherpaOfflineAsr<Moonshine>
    {
        public Moonshine(IAudioEditor audioEditor, ILogger<Moonshine> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(Moonshine);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("encoder.onnx", "tokens.txt"))
                {
                    return false;
                }

                OfflineRecognizerConfig config = new OfflineRecognizerConfig();
                config.ModelConfig.Moonshine.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                string mergedDecoder = Path.Combine(this.ModelFileFoler, "merged-decoder.onnx");
                if (File.Exists(mergedDecoder))
                {
                    config.ModelConfig.Moonshine.MergedDecoder = mergedDecoder;
                }
                else
                {
                    if (!this.CheckModelFiles("preprocessor.onnx", "uncached-decoder.onnx", "cached-decoder.onnx"))
                    {
                        return false;
                    }

                    config.ModelConfig.Moonshine.Preprocessor = Path.Combine(this.ModelFileFoler, "preprocessor.onnx");
                    config.ModelConfig.Moonshine.UncachedDecoder = Path.Combine(this.ModelFileFoler, "uncached-decoder.onnx");
                    config.ModelConfig.Moonshine.CachedDecoder = Path.Combine(this.ModelFileFoler, "cached-decoder.onnx");
                }
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
