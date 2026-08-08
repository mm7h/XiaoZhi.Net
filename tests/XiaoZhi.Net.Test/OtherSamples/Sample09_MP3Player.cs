using MP3Sharp;
using NAudio.Wave;
using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample09_MP3Player
    {
        private const string PlayingAudioFilePath = "./audioFile/Perfect.flac";
        public static async Task RunAsync()
        {
            //await TestTheMP3PlayerAsync();
            MediaFactory.InitializeFFmpeg();
            //await TestTheUrlAudioPlayerAsync();
            await TestTheStreamAudioPlayerAsync();
        }

        private static async Task TestTheMP3PlayerAsync()
        {
            int frameDurationMs = 60; // 每帧的时长，单位毫秒

            // 1. 创建 MP3Stream（来自 MP3Sharp）
            using var mp3Stream = new MP3Stream(PlayingAudioFilePath);
            // 2. 创建 NAudio 的播放器
            using var waveOut = new WaveOutEvent();
            // 3. 创建缓冲区提供者，用于接收 PCM 数据
            var provider = new BufferedWaveProvider(new WaveFormat(mp3Stream.Frequency, 16, mp3Stream.ChannelCount));
            waveOut.Init(provider);
            waveOut.Play(); // 开始播放

            int bufferSize = mp3Stream.Frequency * frameDurationMs / 1000 * 2 * mp3Stream.ChannelCount; // 每帧的字节数

            // 4. 创建缓冲区读取数据
            byte[] buffer = new byte[bufferSize * 2];
            int bytesRead;

            Console.WriteLine("Play started.");

            while ((bytesRead = mp3Stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                provider.AddSamples(buffer, 0, bytesRead);
                await Task.Delay(120);
                //// 控制缓冲区大小，避免延迟过大
                //while (provider.BufferedBytes > bufferSize * 2)
                //{
                //    await Task.Delay(60);
                //}
            }

            //// 5. 等待播放完成
            //while (provider.BufferedBytes > 0)
            //{
            //    await Task.Delay(100);
            //}

            Console.WriteLine("Play completed.");
        }

        private static async Task TestTheUrlAudioPlayerAsync()
        {
            IUrlAudioPlayer audioPlayer = MediaFactory.CreateUrlAudioPlayer();

            if (!await audioPlayer.CheckFFmpegInstalledAsync())
            {
                Console.WriteLine("Failed to initialize the ffmpeg.");
                return;
            }
            //audioPlayer.Volume = 0.3f;

            const int SampleRate = 16000;
            const int Channels = 1;
            const int FrameDurationMs = 60;

            using var waveOut = new WaveOutEvent();
            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
            {
                BufferLength = SampleRate * 2 * Channels * 4
            };
            waveOut.Init(provider);
            waveOut.Play();

            audioPlayer.StateChanged += (s) =>
            {
                Console.WriteLine($"StateChanged: {s} at {DateTime.Now:HH:mm:ss.fff}");
            };

            DateTime lastPositionUpdate = DateTime.Now;
            audioPlayer.PositionChanged += (p) =>
            {
                var now = DateTime.Now;
                var timeSinceLastUpdate = (now - lastPositionUpdate).TotalMilliseconds;
                Console.WriteLine($"PositionChanged: {p} (Real time: {timeSinceLastUpdate:F0}ms since last update) at {now:HH:mm:ss.fff}");
                lastPositionUpdate = now;
            };

            int firstCount = 0;
            int lastCount = 0;
            audioPlayer.OnAudioDataAvailable += (pcmData, isFirst, isLast) =>
            {
                var byteData = new byte[pcmData.Length * 4];
                Buffer.BlockCopy(pcmData, 0, byteData, 0, byteData.Length);

                while (provider.BufferedBytes + byteData.Length > provider.BufferLength)
                {
                    Thread.Sleep(100);
                }
                provider.AddSamples(byteData, 0, byteData.Length);

                if (isFirst)
                {
                    firstCount++;
                    Console.WriteLine($"*** First audio frame received #{firstCount} - playback started");
                }
                if (isLast)
                {
                    lastCount++;
                    Console.WriteLine($"*** Last audio frame received #{lastCount} - playback ending (pause/stop/complete)");
                }
            };

            await audioPlayer.LoadAsync(PlayingAudioFilePath, SampleRate, Channels, FrameDurationMs);

            Console.WriteLine("Starting asynchronous playback...");
            Task playbackTask = audioPlayer.PlayAsync();

            Console.WriteLine("Pausing playback after 20 seconds...");
            await Task.Delay(20000);
            await audioPlayer.PauseAsync();

            Console.WriteLine("Resuming playback after 4 seconds...");
            await Task.Delay(4000);
            Task resumedPlaybackTask = audioPlayer.PlayAsync();

            Console.WriteLine("Seeking to 0 seconds to replay after 2 seconds...");
            await Task.Delay(2000);
            await audioPlayer.SeekAsync(TimeSpan.FromSeconds(120));

            Console.WriteLine("Stopping playback after 10 seconds...");
            await Task.Delay(10000);
            await audioPlayer.StopAsync();

            try
            {
                await Task.WhenAll(playbackTask, resumedPlaybackTask);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Playback was canceled as expected.");
            }

            Console.WriteLine("Replaying the audio after 3 seconds...");
            await Task.Delay(3000);
            Console.WriteLine();

            await audioPlayer.LoadAsync(PlayingAudioFilePath, SampleRate, Channels, FrameDurationMs);
            Console.WriteLine("Starting playback and awaiting natural completion...");
            var startTime = DateTime.Now;
            await audioPlayer.PlayAsync();
            var endTime = DateTime.Now;

            Console.WriteLine($"Playback completed after {(endTime - startTime).TotalSeconds:F2} seconds");
            Console.WriteLine($"Summary: First events: {firstCount}, Last events: {lastCount}");

            Console.WriteLine("All url player tests completed.");
            Console.Read();
        }

        private static async Task TestTheStreamAudioPlayerAsync()
        {
            IStreamAudioPlayer audioPlayer = MediaFactory.CreateStreamAudioPlayer();

            if (!await audioPlayer.CheckFFmpegInstalledAsync())
            {
                Console.WriteLine("Failed to initialize the ffmpeg.");
                return;
            }
            audioPlayer.Volume = 0.3f;

            const int SampleRate = 16000;
            const int Channels = 1;
            const int FrameDurationMs = 60;

            using var waveOut = new WaveOutEvent();
            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
            {
                BufferLength = SampleRate * 2 * Channels * 4
            };
            waveOut.Init(provider);
            waveOut.Play();

            audioPlayer.StateChanged += (s) =>
            {
                Console.WriteLine($"StateChanged: {s} at {DateTime.Now:HH:mm:ss.fff}");
            };

            DateTime lastPositionUpdate = DateTime.Now;
            audioPlayer.PositionChanged += (p) =>
            {
                var now = DateTime.Now;
                var timeSinceLastUpdate = (now - lastPositionUpdate).TotalMilliseconds;
                Console.WriteLine($"PositionChanged: {p} (Real time: {timeSinceLastUpdate:F0}ms since last update) at {now:HH:mm:ss.fff}");
                lastPositionUpdate = now;
            };

            audioPlayer.OnAudioDataAvailable += (pcmData, isFirst, isLast) =>
            {
                var byteData = new byte[pcmData.Length * 4];
                Buffer.BlockCopy(pcmData, 0, byteData, 0, byteData.Length);

                var maxBufferedSamples = SampleRate * Channels * 2; // 2 seconds of audio
                while (provider.BufferedBytes > maxBufferedSamples * 4)
                {
                    Thread.Sleep(10);
                }
                provider.AddSamples(byteData, 0, byteData.Length);

                if (isFirst)
                {
                    Console.WriteLine("*** First audio frame received - playback started");
                }
                if (isLast)
                {
                    Console.WriteLine("*** Last audio frame received - playback ending (pause/stop/complete)");
                }
            };

            using (var stream = File.OpenRead(PlayingAudioFilePath))
            {
                await audioPlayer.LoadAsync(stream, SampleRate, Channels, FrameDurationMs);
                Console.WriteLine("Starting asynchronous playback...");
                Task playbackTask = audioPlayer.PlayAsync();

                Console.WriteLine("Pausing playback after 2 seconds...");
                await Task.Delay(2000);
                await audioPlayer.PauseAsync();

                Console.WriteLine("Resuming playback after 2 seconds...");
                await Task.Delay(2000);
                Task resumedPlaybackTask = audioPlayer.PlayAsync();

                Console.WriteLine("Seeking to 0 seconds to replay after 10 seconds...");
                await Task.Delay(10000);
                await audioPlayer.SeekAsync(TimeSpan.Zero);

                Console.WriteLine("Stopping playback after 4 seconds...");
                await Task.Delay(4000);
                await audioPlayer.StopAsync();

                try
                {
                    await Task.WhenAll(playbackTask, resumedPlaybackTask);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Playback was canceled as expected.");
                }

                Console.WriteLine("Stream audio player cannot support to replay the same file stream.");
            }
            Console.WriteLine("All stream player tests completed.");
            Console.Read();
        }
    }
}
