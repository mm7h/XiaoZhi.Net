using MP3Sharp;
using NAudio.Wave;
using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample09_MP3Player
    {
        const string PLAYING_AUDIO_FILE_PATH = "./audioFile/Perfect.flac";
        public static async Task Run()
        {
            //await TestTheMP3Player();
            MediaFactory.InitializeFFmpeg();
            await TestTheUrlAudioPlayer();
            //await TestTheStreamAudioPlayer();
        }

        static async Task TestTheMP3Player()
        {
            int frameDurationMs = 60; // 每帧的时长，单位毫秒

            // 1. 创建 MP3Stream（来自 MP3Sharp）
            using (var mp3Stream = new MP3Stream(PLAYING_AUDIO_FILE_PATH))
            {
                // 2. 创建 NAudio 的播放器
                using (var waveOut = new WaveOutEvent())
                {
                    // 3. 创建缓冲区提供者，用于接收 PCM 数据
                    var provider = new BufferedWaveProvider(new WaveFormat(mp3Stream.Frequency, 16, mp3Stream.ChannelCount));
                    waveOut.Init(provider);
                    waveOut.Play(); // 开始播放

                    int bufferSize = (mp3Stream.Frequency * frameDurationMs / 1000) * 2 * mp3Stream.ChannelCount; // 每帧的字节数

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
            }
        }

        static async Task TestTheUrlAudioPlayer()
        {
            IUrlAudioPlayer audioPlayer = MediaFactory.CreateUrlAudioPlayer();

            if (!audioPlayer.CheckFFmpegInstalled())
            {
                Console.WriteLine("Failed to initialize the ffmpeg.");
                return;
            }
            //audioPlayer.Volume = 0.3f;

            const int sampleRate = 16000;
            const int channels = 1;
            const int frameDurationMs = 60;

            using (var waveOut = new WaveOutEvent())
            {
                var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
                provider.BufferLength = sampleRate * 2 * channels * 4;
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

                await audioPlayer.LoadAsync(PLAYING_AUDIO_FILE_PATH, sampleRate, channels, frameDurationMs);

                Console.WriteLine("Starting non-blocking playback...");
                audioPlayer.Play(false); // Non-blocking

                Console.WriteLine("Pausing playback after 2 seconds...");
                await Task.Delay(2000);
                audioPlayer.Pause();

                Console.WriteLine("Resuming playback after 2 seconds...");
                await Task.Delay(2000);
                audioPlayer.Play();

                Console.WriteLine("Seeking to 0 seconds to replay after 2 seconds...");
                await Task.Delay(2000);
                audioPlayer.Seek(TimeSpan.Zero);

                Console.WriteLine("Stopping playback after 4 seconds...");
                await Task.Delay(4000);
                audioPlayer.Stop();

                Console.WriteLine("Replaying the audio after 3 seconds...");
                await Task.Delay(3000);
                Console.WriteLine();

                Console.WriteLine("Starting blocking playback...");
                var startTime = DateTime.Now;
                audioPlayer.Play(true); // This will block until playback completes
                var endTime = DateTime.Now;
                
                Console.WriteLine($"Blocking playback completed after {(endTime - startTime).TotalSeconds:F2} seconds");
                Console.WriteLine($"Summary: First events: {firstCount}, Last events: {lastCount}");

                Console.WriteLine("All url player tests completed.");
                Console.Read();
            }
        }

        static async Task TestTheStreamAudioPlayer()
        {
            IStreamAudioPlayer audioPlayer = MediaFactory.CreateStreamAudioPlayer();

            if (!audioPlayer.CheckFFmpegInstalled())
            {
                Console.WriteLine("Failed to initialize the ffmpeg.");
                return;
            }
            audioPlayer.Volume = 0.3f;

            const int sampleRate = 16000;
            const int channels = 1;
            const int frameDurationMs = 60;

            using (var waveOut = new WaveOutEvent())
            {
                var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
                provider.BufferLength = sampleRate * 2 * channels * 4;
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

                    var maxBufferedSamples = sampleRate * channels * 2; // 2 seconds of audio
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

                using (var stream = File.OpenRead(PLAYING_AUDIO_FILE_PATH))
                {
                    await audioPlayer.LoadAsync(stream, sampleRate, channels, frameDurationMs);
                    Console.WriteLine("Starting non-blocking playback...");
                    audioPlayer.Play(false); // Non-blocking

                    Console.WriteLine("Pausing playback after 2 seconds...");
                    await Task.Delay(2000);
                    audioPlayer.Pause();

                    Console.WriteLine("Resuming playback after 2 seconds...");
                    await Task.Delay(2000);
                    audioPlayer.Play();

                    Console.WriteLine("Seeking to 0 seconds to replay after 10 seconds...");
                    await Task.Delay(10000);
                    audioPlayer.Seek(TimeSpan.Zero);

                    Console.WriteLine("Stopping playback after 4 seconds...");
                    await Task.Delay(4000);
                    audioPlayer.Stop();

                    Console.WriteLine("Stream audio player cannot support to replay the same file stream.");
                }
                Console.WriteLine("All stream player tests completed.");
                Console.Read();
            }


        }
    }
}
