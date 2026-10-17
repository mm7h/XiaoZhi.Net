using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal sealed class Vits : BaseSherpaTts<Vits>
    {
        public Vits(IAudioEditor audioEditor, ILogger<Vits> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(Vits);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("model.onnx", "tokens.txt"))
                {
                    return false;
                }

                OfflineTtsConfig config = new OfflineTtsConfig();
                config.Model.Vits.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
                config.Model.Vits.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
                config.Model.Vits.Lexicon = this.OptionalFilePath("lexicon.txt");
                config.Model.Vits.DataDir = this.OptionalDirectoryPath("espeak-ng-data");
                config.Model.Vits.DictDir = this.OptionalDirectoryPath("dict");
                config.Model.Vits.NoiseScale = modelSetting.Config.GetConfigValueOrDefault("NoiseScale", 0.667F);
                config.Model.Vits.NoiseScaleW = modelSetting.Config.GetConfigValueOrDefault("NoiseScaleW", 0.8F);
                config.Model.Vits.LengthScale = modelSetting.Config.GetConfigValueOrDefault("LengthScale", 1.0F);
                config.RuleFsts = this.OptionalFilePath("rule.fst");
                config.RuleFars = this.OptionalFilePath("rule.far");
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
