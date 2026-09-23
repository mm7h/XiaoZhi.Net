using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal sealed class Matcha : BaseSherpaTts<Matcha>
    {
        public Matcha(IAudioEditor audioEditor, ILogger<Matcha> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(Matcha);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles("acoustic-model.onnx", "vocoder.onnx", "tokens.txt"))
                {
                    return false;
                }

                OfflineTtsConfig config = new OfflineTtsConfig();
                config.Model.Matcha.AcousticModel = Path.Combine(this.ModelFileFoler, "acoustic-model.onnx");
                config.Model.Matcha.Vocoder = Path.Combine(this.ModelFileFoler, "vocoder.onnx");
                config.Model.Matcha.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
                config.Model.Matcha.Lexicon = this.OptionalFilePath("lexicon.txt");
                config.Model.Matcha.DataDir = this.OptionalDirectoryPath("espeak-ng-data");
                config.Model.Matcha.DictDir = this.OptionalDirectoryPath("dict");
                config.Model.Matcha.NoiseScale = modelSetting.Config.GetConfigValueOrDefault("NoiseScale", 0.667F);
                config.Model.Matcha.LengthScale = modelSetting.Config.GetConfigValueOrDefault("LengthScale", 1.0F);
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
