using XiaoZhi.Net.Server.Media.Utilities.Extensions;

namespace XiaoZhi.Net.Server.Media.Exceptions;

/// <summary>
/// 表示内部 FFmpeg 处理过程中发生错误时引发的异常。
/// <para>实现：<see cref="Exception"/>。</para>
/// </summary>
internal class FFmpegException : Exception
{
    /// <summary>
    /// 初始化 <see cref="FFmpegException"/>。
    /// </summary>
    public FFmpegException()
    {
    }

    /// <summary>
    /// 使用指定的异常消息初始化 <see cref="FFmpegException"/>。
    /// </summary>
    /// <param name="message">表示异常消息的 <c>string</c>。</param>
    public FFmpegException(string message) : base(message)
    {
    }

    /// <summary>
    /// 使用指定的错误或状态代码初始化 <see cref="FFmpegException"/>。
    /// </summary>
    /// <param name="code">FFmpeg 错误或状态代码。</param>
    public FFmpegException(int code) : base(code.FFErrorToText())
    {
    }
}
