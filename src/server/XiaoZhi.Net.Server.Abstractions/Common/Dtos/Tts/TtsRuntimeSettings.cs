namespace XiaoZhi.Net.Server.Abstractions.Common.Dtos.Tts
{
    /// <summary>
    /// 可在运行时局部更新的 TTS 参数。值为 null 表示保持当前值。
    /// </summary>
    public sealed record TtsRuntimeSettings(
        string? Voice = null,
        float? SpeechRate = null,
        float? Pitch = null);
}
