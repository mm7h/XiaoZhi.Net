namespace XiaoZhi.Net.PerformanceTest.Execution;

internal sealed class PerformanceStageException : Exception
{
    public PerformanceStageException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}
