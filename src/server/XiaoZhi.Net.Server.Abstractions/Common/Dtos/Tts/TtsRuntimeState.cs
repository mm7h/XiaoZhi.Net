using XiaoZhi.Net.Server.Abstractions.Common.Enums.Tts;

namespace XiaoZhi.Net.Server.Abstractions.Common.Dtos.Tts
{
    /// <summary>
    /// 当前会话 TTS 的运行状态。
    /// </summary>
    public sealed record TtsRuntimeState(TtsProviderType ProviderType, TtsRuntimeSettings Settings, bool CanRebuild)
    {
        public static TtsRuntimeState Unsupported { get; } = new(
            TtsProviderType.Unknown,
            new TtsRuntimeSettings(),
            false);
    }
}
