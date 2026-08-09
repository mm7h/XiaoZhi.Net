namespace XiaoZhi.Net.Server.Media.Abstractions.Exceptions;

/// <summary>
/// 音频播放解码工作池没有可用容量时引发的异常。
/// </summary>
public sealed class AudioPlaybackCapacityExceededException : InvalidOperationException
{
    /// <summary>
    /// 初始化 <see cref="AudioPlaybackCapacityExceededException"/> 的新实例。
    /// </summary>
    public AudioPlaybackCapacityExceededException()
        : this(AudioPlaybackCapacityExceededReason.ContextLimit)
    {
    }

    /// <summary>
    /// 初始化 <see cref="AudioPlaybackCapacityExceededException"/> 的新实例。
    /// </summary>
    /// <param name="reason">容量不足的具体资源。</param>
    public AudioPlaybackCapacityExceededException(AudioPlaybackCapacityExceededReason reason)
        : base($"Audio playback capacity has been exceeded: {reason}.")
    {
        this.Reason = reason;
    }

    /// <summary>
    /// 获取容量不足的具体资源。
    /// </summary>
    public AudioPlaybackCapacityExceededReason Reason { get; }
}
