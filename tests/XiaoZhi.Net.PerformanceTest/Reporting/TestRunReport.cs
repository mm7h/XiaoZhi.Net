using XiaoZhi.Net.PerformanceTest.Configuration;
using XiaoZhi.Net.PerformanceTest.Execution;

namespace XiaoZhi.Net.PerformanceTest.Reporting;

internal sealed class TestRunReport
{
    private readonly TestOptions _options;
    private readonly IReadOnlyList<RoundResult> _results;

    public TestRunReport(TestOptions options, IReadOnlyList<RoundResult> results)
    {
        this._options = options;
        this._results = results;
    }

    public string Render()
    {
        StageSummary connection = StageSummary.Create("连接", this._results.Select(result => result.Connection));
        StageSummary hello = StageSummary.Create("Hello", this._results.Select(result => result.Hello));
        StageSummary detect = StageSummary.Create("Detect 首帧音频", this._results.Select(result => result.Detect));
        StageSummary audio = StageSummary.Create("固定音频首帧", this._results.Select(result => result.AudioFirstFrame));
        int completed = this._results.Count(result => result.Completed);
        int missing = this._options.TotalRounds - this._results.Count;

        List<string> lines =
        [
            "XiaoZhi.Net 性能测试最终报告",
            $"计划轮次: {this._options.TotalRounds}；已返回结果: {this._results.Count}；完整轮次: {completed}/{this._options.TotalRounds} ({Rate(completed, this._options.TotalRounds)})",
            connection.Render(),
            hello.Render(),
            detect.Render(),
            audio.Render()
        ];

        if (missing > 0)
        {
            lines.Add($"未产生结果的轮次: {missing}");
        }

        IReadOnlyList<string> failureGroups = this._results
            .Where(result => !result.Completed && !string.IsNullOrWhiteSpace(result.Failure))
            .GroupBy(result => result.Failure!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(10)
            .Select(group => $"失败 {group.Count()} 次: {group.Key}")
            .ToArray();
        if (failureGroups.Count > 0)
        {
            lines.Add("失败原因（前 10 项）:");
            lines.AddRange(failureGroups);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Rate(int numerator, int denominator) => denominator == 0 ? "-" : $"{numerator * 100d / denominator:F2}%";
}
