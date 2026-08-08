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
        : base("Audio playback decoder work pool capacity has been exceeded.")
    {
    }
}
