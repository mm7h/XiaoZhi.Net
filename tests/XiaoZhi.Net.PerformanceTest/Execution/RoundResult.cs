namespace XiaoZhi.Net.PerformanceTest.Execution;

internal sealed class RoundResult
{
    public RoundResult(int clientNumber, int roundNumber)
    {
        this.ClientNumber = clientNumber;
        this.RoundNumber = roundNumber;
    }

    public int ClientNumber { get; }
    public int RoundNumber { get; }
    public StageMeasurement Connection { get; set; } = StageMeasurement.Skipped();
    public StageMeasurement Hello { get; set; } = StageMeasurement.Skipped();
    public StageMeasurement Detect { get; set; } = StageMeasurement.Skipped();
    public StageMeasurement AudioFirstFrame { get; set; } = StageMeasurement.Skipped();
    public bool Completed { get; set; }
    public string? Failure { get; set; }
}
