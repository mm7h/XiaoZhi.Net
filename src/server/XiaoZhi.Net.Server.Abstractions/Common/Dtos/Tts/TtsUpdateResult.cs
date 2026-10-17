namespace XiaoZhi.Net.Server.Abstractions.Common.Dtos.Tts
{
    /// <summary>
    /// TTS 运行时更新结果。
    /// </summary>
    public sealed record TtsUpdateResult(bool Succeeded, string? Message = null);
}
