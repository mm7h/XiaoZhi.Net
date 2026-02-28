namespace XiaoZhi.Net.Server.Media.Utilities.Extensions;

internal static class ThreadExtensions
{
    private const int BreakerCheckIntervalMs = 50;

    public static void EnsureThreadDone(this Thread thread, Func<bool>? breaker = default)
    {
        if (breaker is null)
        {
            thread.Join();
            return;
        }

        while (thread.IsAlive)
        {
            if (breaker())
            {
                break;
            }

            thread.Join(BreakerCheckIntervalMs);
        }
    }
}
