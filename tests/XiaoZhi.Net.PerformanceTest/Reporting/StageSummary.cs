using XiaoZhi.Net.PerformanceTest.Execution;

namespace XiaoZhi.Net.PerformanceTest.Reporting;

internal sealed class StageSummary
{
    private readonly string _name;
    private readonly int _attempted;
    private readonly int _successes;
    private readonly int _skipped;
    private readonly IReadOnlyList<TimeSpan> _durations;

    private StageSummary(string name, int attempted, int successes, int skipped, IReadOnlyList<TimeSpan> durations)
    {
        this._name = name;
        this._attempted = attempted;
        this._successes = successes;
        this._skipped = skipped;
        this._durations = durations;
    }

    public static StageSummary Create(string name, IEnumerable<StageMeasurement> measurements)
    {
        StageMeasurement[] entries = measurements.ToArray();
        IReadOnlyList<TimeSpan> durations = entries
            .Where(entry => entry.Succeeded && entry.Duration is not null)
            .Select(entry => entry.Duration!.Value)
            .OrderBy(duration => duration)
            .ToArray();
        return new StageSummary(
            name,
            entries.Count(entry => entry.Attempted),
            entries.Count(entry => entry.Succeeded),
            entries.Count(entry => !entry.Attempted),
            durations);
    }

    public string Render()
    {
        string common = $"{this._name}: {this._successes}/{this._attempted} ({Rate(this._successes, this._attempted)})，跳过 {this._skipped}";
        if (this._durations.Count == 0)
        {
            return common + "；无成功时延样本";
        }

        return common
            + $"；平均 {Format(this._durations.Average(duration => duration.TotalMilliseconds))}"
            + $"，最小 {Format(this._durations[0].TotalMilliseconds)}"
            + $"，P50 {Format(this.Percentile(0.50).TotalMilliseconds)}"
            + $"，P95 {Format(this.Percentile(0.95).TotalMilliseconds)}"
            + $"，P99 {Format(this.Percentile(0.99).TotalMilliseconds)}"
            + $"，最大 {Format(this._durations[^1].TotalMilliseconds)}";
    }

    private TimeSpan Percentile(double quantile)
    {
        int index = Math.Clamp((int)Math.Ceiling(quantile * this._durations.Count) - 1, 0, this._durations.Count - 1);
        return this._durations[index];
    }

    private static string Rate(int numerator, int denominator) => denominator == 0 ? "-" : $"{numerator * 100d / denominator:F2}%";
    private static string Format(double milliseconds) => $"{milliseconds:F1} ms";
}
