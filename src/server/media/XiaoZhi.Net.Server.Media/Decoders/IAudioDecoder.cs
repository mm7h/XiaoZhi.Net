using XiaoZhi.Net.Server.Media.Common.Models;

namespace XiaoZhi.Net.Server.Media.Decoders;

/// <summary>
/// 表示可从给定音频源解码音频帧，并支持请求中断和重置中断状态的接口。
/// <para>实现：<see cref="IDisposable"/>。</para>
/// </summary>
internal interface IAudioDecoder : IDisposable
{
    /// <summary>
    /// 获取已加载音频源的信息。
    /// </summary>
    AudioStreamInfo StreamInfo { get; }

    /// <summary>
    /// 从已加载的音频源解码下一个可用的音频帧。
    /// </summary>
    /// <returns>新的 <see cref="AudioDecoderResult"/> 数据。</returns>
    AudioDecoderResult DecodeNextFrame();

    /// <summary>
    /// 获取当前解码器是否已经观察到 FFmpeg I/O 中断返回。
    /// </summary>
    bool WasInterrupted { get; }

    /// <summary>
    /// 获取最近一次 FFmpeg I/O 中断的原因。
    /// </summary>
    AudioDecoderInterruptionReason InterruptionReason { get; }

    /// <summary>
    /// 尝试将音频流定位到指定位置；定位成功时返回 <c>true</c>，否则返回 <c>false</c>。
    /// </summary>
    /// <param name="position">目标定位位置。</param>
    /// <param name="error">定位音频流时产生的错误信息。</param>
    /// <returns>定位成功时为 <c>true</c>，否则为 <c>false</c>。</returns>
    bool TrySeek(TimeSpan position, out string? error);

    /// <summary>
    /// 请求中断当前正在执行的阻塞解码操作。
    /// </summary>
    void RequestInterrupt();

    /// <summary>
    /// 在被中断的操作返回后重置中断状态，以允许后续解码或定位操作继续执行。
    /// </summary>
    void ResetInterrupt();

    /// <summary>
    /// 在观察到中断后清理解码器内部缓冲区。
    /// </summary>
    void FlushAfterInterrupt();
}
