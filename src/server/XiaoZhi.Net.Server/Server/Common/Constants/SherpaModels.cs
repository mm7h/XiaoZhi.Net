using XiaoZhi.Net.Server.Providers.ASR.Sherpa;
using XiaoZhi.Net.Server.Providers.TTS.Sherpa;
using XiaoZhi.Net.Server.Providers.VAD.Sherpa;

namespace XiaoZhi.Net.Server.Common.Constants
{
    internal static class SherpaModels
    {
        public static string[] VadModels = [nameof(Silero)];
        public static string[] AsrModels = [nameof(SenseVoice), nameof(Paraformer)];
        public static string[] TtsModels = [nameof(Kokoro)];
    }
}
