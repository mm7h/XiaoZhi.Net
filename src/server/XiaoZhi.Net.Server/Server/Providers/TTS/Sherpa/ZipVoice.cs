using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal sealed class ZipVoice : BaseSherpaTts<ZipVoice>
    {
        public ZipVoice(IAudioEditor audioEditor, ILogger<ZipVoice> logger) : base(audioEditor, logger) { }
        public override string ModelName => nameof(ZipVoice);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelFiles(
                    "tokens.txt",
                    "encoder.onnx",
                    "decoder.onnx",
                    "vocoder.onnx",
                    "lexicon.txt",
                    "reference.wav",
                    "reference.txt")
                    || !this.CheckModelDirectories("espeak-ng-data"))
                {
                    return false;
                }

                OfflineTtsConfig config = new OfflineTtsConfig();
                config.Model.ZipVoice.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
                config.Model.ZipVoice.Encoder = Path.Combine(this.ModelFileFoler, "encoder.onnx");
                config.Model.ZipVoice.Decoder = Path.Combine(this.ModelFileFoler, "decoder.onnx");
                config.Model.ZipVoice.Vocoder = Path.Combine(this.ModelFileFoler, "vocoder.onnx");
                config.Model.ZipVoice.Lexicon = Path.Combine(this.ModelFileFoler, "lexicon.txt");
                config.Model.ZipVoice.DataDir = Path.Combine(this.ModelFileFoler, "espeak-ng-data");
                config.Model.ZipVoice.FeatScale = modelSetting.Config.GetConfigValueOrDefault("FeatScale", 0.1F);
                config.Model.ZipVoice.Tshift = modelSetting.Config.GetConfigValueOrDefault("Tshift", 0.5F);
                config.Model.ZipVoice.TargetRms = modelSetting.Config.GetConfigValueOrDefault("TargetRms", 0.1F);
                config.Model.ZipVoice.GuidanceScale = modelSetting.Config.GetConfigValueOrDefault("GuidanceScale", 1.0F);

                string referenceText = File.ReadAllText(Path.Combine(this.ModelFileFoler, "reference.txt")).Trim();
                if (string.IsNullOrWhiteSpace(referenceText))
                {
                    throw new InvalidDataException("ZipVoice 的 reference.txt 不能为空。");
                }
                (float[] samples, int sampleRate) = ReferenceWaveReader.Read(Path.Combine(this.ModelFileFoler, "reference.wav"));
                OfflineTtsGenerationConfig generationConfig = new OfflineTtsGenerationConfig
                {
                    ReferenceAudio = samples,
                    ReferenceSampleRate = sampleRate,
                    ReferenceText = referenceText,
                    NumSteps = modelSetting.Config.GetConfigValueOrDefault("NumSteps", 4)
                };
                generationConfig.Extra["min_char_in_sentence"] = modelSetting.Config.GetConfigValueOrDefault("MinCharInSentence", "10");
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
