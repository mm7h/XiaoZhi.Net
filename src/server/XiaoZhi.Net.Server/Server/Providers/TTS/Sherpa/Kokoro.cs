using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using System.Linq;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal sealed class Kokoro : BaseSherpaTts<Kokoro>, ITts
    {
       

        public Kokoro(ILogger<Kokoro> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(Kokoro);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelExist())
                {
                    return false;
                }
                var config = new OfflineTtsConfig();
                config.Model.Kokoro.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
                config.Model.Kokoro.Voices = Path.Combine(this.ModelFileFoler, "voices.bin");
                config.Model.Kokoro.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
                config.Model.Kokoro.DataDir = Path.Combine(this.ModelFileFoler, "espeak-ng-data");
                config.Model.Kokoro.DictDir = Path.Combine(this.ModelFileFoler, "dict");

                string lexicons = modelSetting.Config.Lexicons ?? "";
                if (!string.IsNullOrEmpty(lexicons))
                {
                    string lexiconPath = string.Join(',', lexicons.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(l => Path.Combine(this.ModelFileFoler, l)));
                    config.Model.Kokoro.Lexicon = lexiconPath;
                }

                this.Build(config, modelSetting);

                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }
    }
}
