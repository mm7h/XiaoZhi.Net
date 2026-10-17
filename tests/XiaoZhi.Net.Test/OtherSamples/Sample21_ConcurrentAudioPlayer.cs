using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Exceptions;

namespace XiaoZhi.Net.Test.OtherSamples
{
    /// <summary>
    /// 验证多个独立播放器共享解码调度器时的并发、取消和资源限制行为
    /// </summary>
    internal static class Sample21_ConcurrentAudioPlayer
    {
        private const int Channels = 1;
        private const int FrameDurationMs = 60;
        private const int PlayerCount = 20;
        private const int SampleRate = 16000;

        private const string PlayingAudioFilePath = "./audioFile/Perfect.flac";

        public static async Task RunAsync()
        {
            MediaFactory.InitializeFFmpeg();

            await ConcurrentPlayAsync(PlayingAudioFilePath);
            await VerifyExternalCancellationAsync(PlayingAudioFilePath);
            await VerifyContextCapacityAsync(PlayingAudioFilePath);
            await VerifyGlobalBufferWakeupAsync(PlayingAudioFilePath);
            await UrlTimeOutTestAsync();

            Console.WriteLine("Sample21 completed.");
        }

        private static async Task ConcurrentPlayAsync(string audioPath)
        {
            IUrlAudioPlayer[] players = Enumerable.Range(0, PlayerCount)
                .Select(_ => MediaFactory.CreateUrlAudioPlayer())
                .ToArray();
            int[] frameCounts = new int[players.Length];

            try
            {
                int playerIndex = 0;

                if (!await players[playerIndex].CheckFFmpegInstalledAsync())
                {
                    throw new InvalidOperationException("FFmpeg 初始化失败。");
                }

                for (int index = 0; index < players.Length; index++)
                {
                    int capturedIndex = index;
                    players[index].OnAudioDataAvailable += (_, _, _) =>
                        Interlocked.Increment(ref frameCounts[capturedIndex]);
                }

                bool[] loaded = await Task.WhenAll(players.Select(LoadAsync));
                if (loaded.Any(static value => !value))
                {
                    throw new InvalidOperationException("至少一个并发播放器未能加载 Sample09 音频文件。");
                }

                Task[] playbackTasks = players.Select(ObserveCancellationAsync).ToArray();

                await Task.Delay(TimeSpan.FromSeconds(2));
                await players[playerIndex].PauseAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                _ = players[playerIndex].PlayAsync();
                await players[playerIndex].SeekAsync(TimeSpan.FromSeconds(1));
                await Task.Delay(TimeSpan.FromMilliseconds(300));

                await Task.WhenAll(players.Select(static player => player.StopAsync()));
                await Task.WhenAll(playbackTasks).WaitAsync(TimeSpan.FromSeconds(10));

                if (frameCounts.Any(static count => count == 0))
                {
                    throw new InvalidOperationException("至少一个并发播放器没有输出 PCM 帧。");
                }

                Console.WriteLine($"Concurrent playback passed: {PlayerCount} players; frames={string.Join(',', frameCounts)}");
            }
            finally
            {
                foreach (IUrlAudioPlayer player in players)
                {
                    player.Dispose();
                }
            }

            async Task<bool> LoadAsync(IUrlAudioPlayer player)
            {
                return await player.LoadAsync(audioPath, SampleRate, Channels, FrameDurationMs);
            }
        }

        private static async Task VerifyExternalCancellationAsync(string audioPath)
        {
            using IUrlAudioPlayer player = MediaFactory.CreateUrlAudioPlayer();
            if (!await player.LoadAsync(audioPath, SampleRate, Channels, FrameDurationMs))
            {
                throw new InvalidOperationException("外部取消测试无法加载 Sample09 音频文件。");
            }

            using CancellationTokenSource cancellationSource = new();
            Task playbackTask = player.PlayAsync(cancellationSource.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            cancellationSource.Cancel();

            try
            {
                await playbackTask.WaitAsync(TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("外部取消不应被视为自然播放完成。");
            }
            catch (AudioPlaybackCanceledException)
            {
                Console.WriteLine("External cancellation passed.");
            }
        }

        private static async Task VerifyContextCapacityAsync(string audioPath)
        {
            (IDisposable scheduler, IUrlAudioPlayer[] players) = CreateLimitedPlayers(
                maxPlaybackContexts: 2,
                maxGlobalBufferedBytes: 256L * 1024 * 1024,
                playerCount: 3);

            try
            {
                bool[] loaded = await Task.WhenAll(players.Take(2).Select(LoadAsync));
                if (loaded.Any(static value => !value))
                {
                    throw new InvalidOperationException("容量测试中的前两路播放器加载失败。");
                }

                try
                {
                    await LoadAsync(players[2]);
                    throw new InvalidOperationException("超过播放上下文上限时应拒绝加载。");
                }
                catch (AudioPlaybackCapacityExceededException exception)
                    when (exception.Reason == AudioPlaybackCapacityExceededReason.ContextLimit)
                {
                    Console.WriteLine("Playback context capacity passed.");
                }
            }
            finally
            {
                foreach (IUrlAudioPlayer player in players)
                {
                    player.Dispose();
                }

                scheduler.Dispose();
            }

            async Task<bool> LoadAsync(IUrlAudioPlayer player)
            {
                return await player.LoadAsync(audioPath, SampleRate, Channels, FrameDurationMs);
            }
        }

        private static async Task VerifyGlobalBufferWakeupAsync(string audioPath)
        {
            (IDisposable scheduler, IUrlAudioPlayer[] players) = CreateLimitedPlayers(
                maxPlaybackContexts: 4,
                maxGlobalBufferedBytes: 8_000,
                playerCount: 3);
            int[] frameCounts = new int[players.Length];

            try
            {
                for (int index = 0; index < players.Length; index++)
                {
                    int capturedIndex = index;
                    players[index].OnAudioDataAvailable += (_, _, _) =>
                        Interlocked.Increment(ref frameCounts[capturedIndex]);
                }

                bool[] loaded = await Task.WhenAll(players.Select(LoadAsync));
                if (loaded.Any(static value => !value))
                {
                    throw new InvalidOperationException("低全局缓冲上限下播放器加载失败。");
                }

                Task[] playbackTasks = players.Select(ObserveCancellationAsync).ToArray();
                await Task.Delay(TimeSpan.FromSeconds(2));
                await Task.WhenAll(players.Select(static player => player.StopAsync()));
                await Task.WhenAll(playbackTasks).WaitAsync(TimeSpan.FromSeconds(10));

                if (frameCounts.Any(static count => count == 0))
                {
                    throw new InvalidOperationException("低全局缓冲上限下存在未被唤醒的播放器。");
                }

                Console.WriteLine($"Global buffer wake-up passed: frames={string.Join(',', frameCounts)}");
            }
            finally
            {
                foreach (IUrlAudioPlayer player in players)
                {
                    player.Dispose();
                }

                scheduler.Dispose();
            }

            async Task<bool> LoadAsync(IUrlAudioPlayer player)
            {
                return await player.LoadAsync(audioPath, SampleRate, Channels, FrameDurationMs);
            }
        }

        private static async Task UrlTimeOutTestAsync()
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<TcpClient> acceptedClientTask = listener.AcceptTcpClientAsync();
            using IUrlAudioPlayer player = MediaFactory.CreateUrlAudioPlayer();

            Stopwatch stopwatch = Stopwatch.StartNew();
            Task<bool> loadTask = player.LoadAsync(
                $"http://127.0.0.1:{port}/stall",
                SampleRate,
                Channels,
                FrameDurationMs);

            using TcpClient acceptedClient = await acceptedClientTask.WaitAsync(TimeSpan.FromSeconds(3));
            bool loaded = await loadTask.WaitAsync(TimeSpan.FromSeconds(15));
            stopwatch.Stop();

            if (loaded || stopwatch.Elapsed > TimeSpan.FromSeconds(13))
            {
                throw new InvalidOperationException("URL 打开超时未在预期时间内返回失败。");
            }

            Console.WriteLine($"URL open timeout passed: {stopwatch.ElapsedMilliseconds}ms");
        }

        private static (IDisposable Scheduler, IUrlAudioPlayer[] Players) CreateLimitedPlayers(
            int maxPlaybackContexts,
            long maxGlobalBufferedBytes,
            int playerCount)
        {
            Assembly mediaAssembly = typeof(MediaFactory).Assembly;
            Type schedulerType = mediaAssembly.GetType("XiaoZhi.Net.Server.Media.Players.WorkPool.AudioDecodeScheduler")
                ?? throw new InvalidOperationException("无法定位内部解码调度器。");
            Type playerType = mediaAssembly.GetType("XiaoZhi.Net.Server.Media.Players.UrlAudioPlayer")
                ?? throw new InvalidOperationException("无法定位内部 URL 播放器。");
            object scheduler = Activator.CreateInstance(
                schedulerType,
                new AudioPlayerOptions
                {
                    DecoderWorkerCount = 2,
                    MaxPlaybackContexts = maxPlaybackContexts,
                    MaxConcurrentLoads = 1,
                    MaxGlobalBufferedBytes = maxGlobalBufferedBytes
                }) ?? throw new InvalidOperationException("无法创建内部解码调度器。");
            Type loggerType = typeof(NullLogger<>).MakeGenericType(playerType);
            object logger = Activator.CreateInstance(loggerType)
                ?? throw new InvalidOperationException("无法创建空日志记录器。");
            IUrlAudioPlayer[] players = Enumerable.Range(0, playerCount)
                .Select(_ => (IUrlAudioPlayer)(Activator.CreateInstance(playerType, scheduler, logger)
                    ?? throw new InvalidOperationException("无法创建内部 URL 播放器。")))
                .ToArray();

            return ((IDisposable)scheduler, players);
        }

        private static async Task ObserveCancellationAsync(IUrlAudioPlayer player)
        {
            try
            {
                await player.PlayAsync();
            }
            catch (AudioPlaybackCanceledException)
            {
                // StopAsync 是本用例的预期结束方式。
            }
        }
    }
}
