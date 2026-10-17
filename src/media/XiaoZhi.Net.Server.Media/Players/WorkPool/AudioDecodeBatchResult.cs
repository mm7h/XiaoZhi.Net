namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

/// <summary>
/// 表示一次短批次解码后的后续调度决定。
/// </summary>
internal readonly record struct AudioDecodeBatchResult(bool ShouldReschedule, AudioDecodeWorkPriority Priority)
{
    public static AudioDecodeBatchResult Completed { get; } = new(false, AudioDecodeWorkPriority.Refill);

    public static AudioDecodeBatchResult Refill { get; } = new(true, AudioDecodeWorkPriority.Refill);
}
