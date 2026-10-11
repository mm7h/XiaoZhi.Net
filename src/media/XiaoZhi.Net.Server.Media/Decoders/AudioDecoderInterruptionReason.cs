namespace XiaoZhi.Net.Server.Media.Decoders;

/// <summary>
/// 指示 FFmpeg 解码操作被中断的原因。
/// </summary>
internal enum AudioDecoderInterruptionReason
{
    /// <summary>
    /// 未观察到中断。
    /// </summary>
    None,

    /// <summary>
    /// 因停止、定位或取消请求而中断。
    /// </summary>
    Requested,

    /// <summary>
    /// URL 音频源在配置时间内没有读取进展。
    /// </summary>
    UrlReadTimeout
}
