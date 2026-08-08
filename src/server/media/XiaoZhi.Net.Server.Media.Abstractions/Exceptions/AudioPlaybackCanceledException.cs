namespace XiaoZhi.Net.Server.Media.Abstractions.Exceptions;

/// <summary>
/// 音频播放解码工作被取消时引发的异常。
/// </summary>
public sealed class AudioPlaybackCanceledException : OperationCanceledException
{
    /// <summary>
    /// 初始化 <see cref="AudioPlaybackCanceledException"/> 的新实例。
    /// </summary>
    public AudioPlaybackCanceledException(CancellationToken cancellationToken)
        : base("Audio playback decoder work was canceled.", cancellationToken)
    {
    }
}
