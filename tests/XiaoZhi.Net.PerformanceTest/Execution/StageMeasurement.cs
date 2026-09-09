namespace XiaoZhi.Net.PerformanceTest.Execution;

internal sealed record StageMeasurement(bool Attempted, bool Succeeded, TimeSpan? Duration, string? Failure)
{
    public static StageMeasurement Success(TimeSpan duration) => new(true, true, duration, null);
    public static StageMeasurement Failed(TimeSpan duration, string reason) => new(true, false, duration, reason);
    public static StageMeasurement Skipped() => new(false, false, null, "Skipped");
}
