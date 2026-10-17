using System.Linq;
using System.Text.RegularExpressions;
using XiaoZhi.Net.Server.Providers.ASR.Sherpa;
using XiaoZhi.Net.Server.Providers.TTS.Sherpa;
using XiaoZhi.Net.Server.Providers.VAD.Sherpa;

namespace XiaoZhi.Net.Server.Common.Constants
{
    internal static class SherpaModels
    {
        private static readonly string[] s_vadModels = [nameof(Silero), nameof(TenVad)];
        private static readonly string[] s_asrModels = [
            nameof(SenseVoice), nameof(Paraformer),
            nameof(OfflineTransducer), nameof(NemoCtc), nameof(Whisper), nameof(Tdnn), nameof(TeleSpeechCtc),
            nameof(Moonshine), nameof(FireRedAsr), nameof(Dolphin), nameof(ZipformerCtc), nameof(Canary), nameof(WenetCtc),
            nameof(OmnilingualAsrCtc), nameof(MedAsrCtc), nameof(FunAsrNano), nameof(FireRedAsrCtc),
            nameof(Qwen3Asr), nameof(CohereTranscribe),
            nameof(OnlineTransducer), nameof(OnlineParaformer), nameof(OnlineZipformer2Ctc), nameof(OnlineNemoCtc), nameof(OnlineToneCtc)];
        private static readonly string[] s_ttsModels = [nameof(Kokoro), nameof(Vits), nameof(Matcha), nameof(Kitten), nameof(ZipVoice), nameof(Pocket), nameof(Supertonic)];

        public static bool IsSherpaModel(string providerType, string modelName)
        {
            string normalizedModelName = ToKebabCase(modelName);
            return providerType.ToLowerInvariant() switch
            {
                "vad" => s_vadModels.Any(model => ToKebabCase(model) == normalizedModelName),
                "asr" => s_asrModels.Any(model => ToKebabCase(model) == normalizedModelName),
                "tts" => s_ttsModels.Any(model => ToKebabCase(model) == normalizedModelName),
                _ => false
            };
        }

        private static string ToKebabCase(string input)
        {
            return string.IsNullOrWhiteSpace(input) ? input : Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
        }
    }
}
