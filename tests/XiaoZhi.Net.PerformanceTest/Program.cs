// thanks https://github.com/joey-zhou/xiaozhi-concurrent

using McMaster.Extensions.CommandLineUtils;
using Serilog;
using XiaoZhi.Net.PerformanceTest.Audio;
using XiaoZhi.Net.PerformanceTest.Configuration;
using XiaoZhi.Net.PerformanceTest.Execution;
using XiaoZhi.Net.PerformanceTest.Reporting;

using CancellationTokenSource shutdown = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

using CommandLineApplication app = new()
{
    Name = "xiaozhi-performance-test",
    Description = "XiaoZhi.Net WebSocket 性能测试客户端"
};

CommandOption serverOption = app.Option(
    "-s|--server <URL>",
    "WebSocket 服务端地址。默认: ws://localhost:4530/xiaozhi/v1/",
    CommandOptionType.SingleValue);
CommandOption clientsOption = app.Option(
    "-c|--clients <COUNT>",
    "并发客户端数。默认: 20",
    CommandOptionType.SingleValue);
CommandOption roundsOption = app.Option(
    "-r|--rounds <COUNT>",
    "每个客户端执行轮数。默认: 1",
    CommandOptionType.SingleValue);
CommandOption audioOption = app.Option(
    "-a|--audio <FILE>",
    "audios 目录中的 WAV 文件名或路径；目录仅有一个 WAV 时可省略。",
    CommandOptionType.SingleValue);

app.OnExecuteAsync(async cancellationToken =>
{
    using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        shutdown.Token);

    try
    {
        TestOptions options = TestOptions.Parse(
            serverOption.Value(),
            clientsOption.Value(),
            roundsOption.Value(),
            audioOption.Value());

        string runDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(runDirectory);
        string logPath = Path.Combine(runDirectory, $"performance-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Async(writeTo => writeTo.File(logPath))
            .CreateLogger();

        try
        {
            AudioCache audioCache = AudioCache.LoadFromOutputDirectory(AppContext.BaseDirectory, options.AudioSelector);
            Console.WriteLine($"服务端: {options.ServerUri}");
            Console.WriteLine($"并发客户端: {options.ClientCount}；每客户端轮数: {options.Rounds}；总轮次: {options.TotalRounds}");
            Console.WriteLine($"音频: {audioCache.Selected.Name}，{audioCache.Selected.FrameCount} 个 Opus 帧，{audioCache.Selected.Format.SampleRate} Hz 单声道");
            Console.WriteLine($"日志: {logPath}");

            PerformanceTestRunner runner = new(options, audioCache.Selected, Log.Logger);
            TestRunReport report = await runner.RunAsync(linkedCancellation.Token);
            Console.WriteLine();
            Console.WriteLine(report.Render());
            return linkedCancellation.IsCancellationRequested ? 130 : 0;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested || cancellationToken.IsCancellationRequested)
    {
        Console.Error.WriteLine("测试已取消。");
        return 130;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"无法启动性能测试: {exception.Message}");
        return 2;
    }
});

try
{
    return await app.ExecuteAsync(args);
}
catch (CommandParsingException exception)
{
    Console.Error.WriteLine($"命令行参数无效: {exception.Message}");
    return 2;
}
