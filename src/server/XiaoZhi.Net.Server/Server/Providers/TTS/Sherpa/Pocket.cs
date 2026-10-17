using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal sealed class Pocket : BaseSherpaTts<Pocket>
    {
        public Pocket(IAudioEditor audioEditor, ILogger<Pocket> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(Pocket);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles(
                    "lm-flow.onnx",
                    "lm-main.onnx",
                    "encoder.onnx",
                    "decoder.onnx",
                    "text-conditioner.onnx",
                    "vocab.json",
                    "token-scores.json",
                    "reference.wav"))
                {
                    return false;
                }

                OfflineTtsConfig config = new OfflineTtsConfig();
                config.Model.Pocket.LmFlow = Path.Combine(this.ModelFileFoler, "lm-flow.onnx");
                config.Model.Pocket.LmMain = Path.Combine(this.ModelFileFoler, "lm-main.onnx");
                config.Model.Pocket.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                config.Model.Pocket.Decoder = Path.Combine(this.ModelFileFoler, "decoder.onnx");
                config.Model.Pocket.TextConditioner = Path.Combine(this.ModelFileFoler, "text-conditioner.onnx");
                config.Model.Pocket.VocabJson = Path.Combine(this.ModelFileFoler, "vocab.json");
                config.Model.Pocket.TokenScoresJson = Path.Combine(this.ModelFileFoler, "token-scores.json");
                config.Model.Pocket.VoiceEmbeddingCacheCapacity = modelSetting.Config.GetConfigValueOrDefault("VoiceEmbeddingCacheCapacity", 50);

                (float[] samples, int sampleRate) = ReferenceWaveReader.Read(Path.Combine(this.ModelFileFoler, "reference.wav"));
                OfflineTtsGenerationConfig generationConfig = new OfflineTtsGenerationConfig
                {
                    ReferenceAudio = samples,
                    ReferenceSampleRate = sampleRate
                };
                generationConfig.Extra["max_reference_audio_len"] = modelSetting.Config.GetConfigValueOrDefault("MaxReferenceAudioLength", 12);
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
