namespace XiaoZhi.Net.Server.Media.Common.Models;

/// <summary>
/// 表示音频解码器读取音频帧时返回的结果结构。
/// </summary>
/// <remarks>
/// 初始化 <see cref="AudioDecoderResult"/> 结构。
/// </remarks>
/// <param name="frame">读取成功时的已解码音频帧。</param>
/// <param name="succeeded">是否成功读取音频帧。</param>
/// <param name="eof">解码器是否已到达文件末尾。</param>
/// <param name="errorMessage">读取音频帧时产生的错误信息。</param>
internal readonly struct AudioDecoderResult(AudioFrame? frame, bool succeeded, bool eof, string? errorMessage = default)
{

    /// <summary>
    /// 获取读取成功时的已解码音频帧。
    /// 当 <see cref="IsSucceeded"/> 为 <c>false</c> 时，应返回 <c>null</c>。
    /// </summary>
    public AudioFrame? Frame { get; } = frame;

    /// <summary>
    /// 获取解码器是否成功读取音频帧。
    /// </summary>
    public bool IsSucceeded { get; } = succeeded;

    /// <summary>
    /// 获取解码器读取音频帧时是否已到达文件末尾（无法继续读取）。
    /// </summary>
    public bool IsEOF { get; } = eof;

    /// <summary>
    /// 获取解码器读取音频帧时的错误信息。
    /// </summary>
    public string? ErrorMessage { get; } = errorMessage;
}
