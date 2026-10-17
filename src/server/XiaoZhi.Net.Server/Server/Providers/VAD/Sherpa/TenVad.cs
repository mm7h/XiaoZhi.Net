using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.VAD.Sherpa
{
    internal sealed class TenVad : BaseSherpaVad<TenVad>, IVad
    {
        public TenVad(ILogger<TenVad> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(TenVad);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("model.onnx"))
                {
                    return false;
                }

                VadModelConfig config = new VadModelConfig();
                config.TenVad.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
                config.TenVad.Threshold = modelSetting.Config.GetConfigValueOrDefault("Threshold", 0.5F);
                config.TenVad.MinSilenceDuration = modelSetting.Config.GetConfigValueOrDefault("SilenceThresholdSecond", 0.5F);
                config.TenVad.MinSpeechDuration = modelSetting.Config.GetConfigValueOrDefault("MinSpeechDurationSecond", 0.25F);
                config.TenVad.MaxSpeechDuration = modelSetting.Config.GetConfigValueOrDefault("MaxSpeechDurationSecond", 5.0F);
                config.TenVad.WindowSize = modelSetting.Config.GetConfigValueOrDefault("WindowSize", 256);
                return this.Build(config, modelSetting);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.TenVad_Build_Failed);
                return false;
            }
        }
    }
}
