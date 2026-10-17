using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal sealed class Supertonic : BaseSherpaTts<Supertonic>
    {
        public Supertonic(IAudioEditor audioEditor, ILogger<Supertonic> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(Supertonic);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles(
                    "duration-predictor.onnx",
                    "text-encoder.onnx",
                    "vector-estimator.onnx",
                    "vocoder.onnx",
                    "tts.json",
                    "unicode-indexer.bin",
                    "voice.bin"))
                {
                    return false;
                }

                OfflineTtsConfig config = new OfflineTtsConfig();
                config.Model.Supertonic.DurationPredictor = Path.Combine(this.ModelFileFoler, "duration-predictor.onnx");
                config.Model.Supertonic.TextEncoder = Path.Combine(this.ModelFileFoler, "text-encoder.onnx");
                config.Model.Supertonic.VectorEstimator = Path.Combine(this.ModelFileFoler, "vector-estimator.onnx");
                config.Model.Supertonic.Vocoder = Path.Combine(this.ModelFileFoler, "vocoder.onnx");
                config.Model.Supertonic.TtsJson = Path.Combine(this.ModelFileFoler, "tts.json");
                config.Model.Supertonic.UnicodeIndexer = Path.Combine(this.ModelFileFoler, "unicode-indexer.bin");
                config.Model.Supertonic.VoiceStyle = Path.Combine(this.ModelFileFoler, "voice.bin");
                OfflineTtsGenerationConfig generationConfig = new OfflineTtsGenerationConfig
                {
                    NumSteps = modelSetting.Config.GetConfigValueOrDefault("NumSteps", 8)
                };
                generationConfig.Extra["lang"] = modelSetting.Config.GetConfigValueOrDefault("Language", "en");
                this.Build(config, modelSetting, generationConfig);
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
