using System;
using System.Threading;

namespace XiaoZhi.Net.Server.AudioPlayer.Utilities.Extensions;

internal static class ThreadExtensions
{
    public static void EnsureThreadDone(this Thread thread, Func<bool> breaker = default)
    {
        while (thread.IsAlive)
        {
            if (breaker != null && breaker())
            {
                break;
            }

            Thread.Sleep(10);
        }
    }
}
