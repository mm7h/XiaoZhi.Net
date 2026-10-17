namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

/// <summary>
/// 解码调度任务的优先级。数值越小，优先级越高。
/// </summary>
internal enum AudioDecodeWorkPriority
{
    StopOrDispose = 0,
    Seek = 1,
    Refill = 2,
    Load = 3
}
