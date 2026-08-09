using XiaoZhi.Net.Server.Media.Players.Contexts;

namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

/// <summary>
/// 表示已进入调度队列的一次播放上下文解码请求。
/// </summary>
internal readonly record struct AudioDecodeScheduleRequest(
    AudioPlaybackContext Context,
    int ScheduleVersion,
    int Generation,
    AudioDecodeWorkPriority Priority,
    long DeadlineTimestamp);
