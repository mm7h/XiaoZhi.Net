namespace XiaoZhi.Net.Server.Common.Contexts.Aliyun
{
    /// <summary>
    /// 阿里云非实时 HTTP TTS 的响应。该类型同时用于普通 JSON 和 SSE data 事件。
    /// </summary>
    internal sealed record AliyunHttpTtsResponse(
        string? RequestId,
        AliyunHttpTtsOutput? Output,
        string? Code,
        string? Message);

    internal sealed record AliyunHttpTtsOutput(
        string? FinishReason,
        string? Type,
        string? OriginalText,
        AliyunHttpTtsAudio? Audio);

    internal sealed record AliyunHttpTtsAudio(
        string? Data,
        string? Url,
        string? Id,
        long? ExpiresAt);
}
