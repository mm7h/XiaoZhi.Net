using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal sealed class Kitten : BaseSherpaTts<Kitten>
    {
        public Kitten(IAudioEditor audioEditor, ILogger<Kitten> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(Kitten);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("model.onnx", "voices.bin", "tokens.txt") || !this.CheckModelDirectories("espeak-ng-data"))
                {
                    return false;
                }

                OfflineTtsConfig config = new OfflineTtsConfig();
                config.Model.Kitten.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
                config.Model.Kitten.Voices = Path.Combine(this.ModelFileFoler, "voices.bin");
                config.Model.Kitten.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
                config.Model.Kitten.DataDir = Path.Combine(this.ModelFileFoler, "espeak-ng-data");
                config.Model.Kitten.LengthScale = modelSetting.Config.GetConfigValueOrDefault("LengthScale", 1.0F);
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
