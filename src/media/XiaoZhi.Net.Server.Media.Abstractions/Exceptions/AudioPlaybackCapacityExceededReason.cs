namespace XiaoZhi.Net.Server.Media.Abstractions.Exceptions;

/// <summary>
/// 指示音频播放容量不足的具体资源。
/// </summary>
public enum AudioPlaybackCapacityExceededReason
{
    /// <summary>
    /// 播放上下文数量已达到上限。
    /// </summary>
    ContextLimit,

    /// <summary>
    /// 全局 PCM 缓冲区已达到上限。
    /// </summary>
    GlobalBufferLimit,

    /// <summary>
    /// 解码调度队列已满。
    /// </summary>
    SchedulerQueueFull
}
