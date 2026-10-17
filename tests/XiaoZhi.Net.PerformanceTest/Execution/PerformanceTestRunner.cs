using System.Collections.Concurrent;
using Serilog;
using XiaoZhi.Net.PerformanceTest.Audio;
using XiaoZhi.Net.PerformanceTest.Configuration;
using XiaoZhi.Net.PerformanceTest.Reporting;

namespace XiaoZhi.Net.PerformanceTest.Execution;

internal sealed class PerformanceTestRunner
{
    private static readonly TimeSpan s_betweenRounds = TimeSpan.FromMilliseconds(500);
    private readonly TestOptions _options;
    private readonly CachedAudio? _audio;
    private readonly ILogger _logger;

    public PerformanceTestRunner(TestOptions options, CachedAudio? audio, ILogger logger)
    {
        this._options = options;
        this._audio = audio;
        this._logger = logger;
    }

    public async Task<TestRunReport> RunAsync(CancellationToken cancellationToken)
    {
        ConcurrentBag<RoundResult> results = [];
        TaskCompletionSource startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource allClientsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource[]? helloRoundGates = this._options.HelloOnly
            ? Enumerable.Range(0, this._options.Rounds)
                .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
                .ToArray()
            : null;
        TaskCompletionSource[]? roundClosedGates = this._options.HelloOnly
            ? Enumerable.Range(0, this._options.Rounds)
                .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
                .ToArray()
            : null;
        int[] helloAttempts = new int[this._options.Rounds];
        int[] roundClosed = new int[this._options.Rounds];
        int activeHelloConnections = 0;
        int peakHelloConnections = 0;
        int readyClients = 0;
        int completedRounds = 0;

        Task[] clients = Enumerable.Range(1, this._options.ClientCount)
            .Select(clientNumber => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref readyClients) == this._options.ClientCount)
                {
                    allClientsReady.TrySetResult();
                }

                await startGate.Task.WaitAsync(cancellationToken);
                for (int roundNumber = 1; roundNumber <= this._options.Rounds; roundNumber++)
                {
                    string deviceId = $"xiaozhi-test-{clientNumber:000000}";
                    XiaoZhiPerformanceClient client = new(this._options.ServerUri, deviceId, this._audio, this._logger);
                    RoundResult result;
                    try
                    {
                        Func<RoundResult, Task>? beforeDisposeAsync = null;
                        if (helloRoundGates is not null)
                        {
                            int roundIndex = roundNumber - 1;
                            TaskCompletionSource gate = helloRoundGates[roundIndex];
                            beforeDisposeAsync = async roundResult =>
                            {
                                if (Interlocked.Increment(ref helloAttempts[roundIndex]) == this._options.ClientCount)
                                {
                                    gate.TrySetResult();
                                }
                                if (roundResult.Hello.Succeeded)
                                {
                                    int active = Interlocked.Increment(ref activeHelloConnections);
                                    int observed;
                                    while ((observed = Volatile.Read(ref peakHelloConnections)) < active
                                        && Interlocked.CompareExchange(ref peakHelloConnections, active, observed) != observed)
                                    {
                                    }
                                    try
                                    {
                                        await gate.Task.WaitAsync(cancellationToken);
                                    }
                                    finally
                                    {
                                        Interlocked.Decrement(ref activeHelloConnections);
                                    }
                                }
                            };
                        }
                        result = await client.ExecuteAsync(clientNumber, roundNumber, this._options.HelloOnly, beforeDisposeAsync, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        result = new RoundResult(clientNumber, roundNumber)
                        {
                            Failure = "测试取消"
                        };
                    }
                    catch (Exception exception)
                    {
                        result = new RoundResult(clientNumber, roundNumber)
                        {
                            Failure = $"测试客户端异常: {exception.GetType().Name}"
                        };
                    }

                    results.Add(result);
                    if (!result.Completed)
                    {
                        this._logger.Warning(
                            "[{DeviceId}] 第 {RoundNumber} 轮失败: {Failure}",
                            deviceId,
                            roundNumber,
                            result.Failure ?? "未知错误");
                    }

                    Interlocked.Increment(ref completedRounds);
                    if (roundClosedGates is not null)
                    {
                        int roundIndex = roundNumber - 1;
                        if (Interlocked.Increment(ref roundClosed[roundIndex]) == this._options.ClientCount)
                        {
                            roundClosedGates[roundIndex].TrySetResult();
                        }
                        await roundClosedGates[roundIndex].Task.WaitAsync(cancellationToken);
                    }
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (roundNumber < this._options.Rounds)
                    {
                        await Task.Delay(s_betweenRounds, cancellationToken);
                    }
                }
            }, CancellationToken.None))
            .ToArray();

        await allClientsReady.Task.WaitAsync(cancellationToken);
        startGate.TrySetResult();

        Task progress = ShowProgressAsync(() => Volatile.Read(ref completedRounds), this._options.TotalRounds, cancellationToken);
        await Task.WhenAll(clients);
        try
        {
            await progress;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return new TestRunReport(this._options, results.OrderBy(result => result.ClientNumber).ThenBy(result => result.RoundNumber).ToArray(), peakHelloConnections);
    }

    private static async Task ShowProgressAsync(Func<int> completed, int total, CancellationToken cancellationToken)
    {
        while (completed() < total && !cancellationToken.IsCancellationRequested)
        {
            Console.Write($"\r进度: {completed()}/{total} 轮已完成");
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        Console.Write($"\r进度: {completed()}/{total} 轮已完成");
    }
}
