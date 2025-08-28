using MP3Sharp;
using NAudio.Wave;
using XiaoZhi.Net.Server.AudioPlayer;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample09_MP3Player
    {
        public static async Task Run()
        {
            //await TestTheMP3Player();
            await TestTheAudioPlayer();
        }

        static async Task TestTheMP3Player()
        {
            string filePath = @"./audioFile/Perfect.mp3";
            int frameDurationMs = 60; // 每帧的时长，单位毫秒

            // 1. 创建 MP3Stream（来自 MP3Sharp）
            using (var mp3Stream = new MP3Stream(filePath))
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

        static async Task TestTheAudioPlayer()
        {
            if (!AudioPlayerFactory.InitializeFFmpeg())
            {
                Console.WriteLine("Failed to initialize the ffmpeg.");
                return;
            }
            //IUrlAudioPlayer audioPlayer = AudioPlayerFactory.CreateUrlAudioPlayer();
            IStreamAudioPlayer audioPlayer = AudioPlayerFactory.CreateStreamAudioPlayer();

            if (!audioPlayer.CheckFFmpegInstalled())
            {
                Console.WriteLine("Failed to initialize the ffmpeg.");
                return;
            }
            audioPlayer.Volume = 0.2f;

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
                    Console.WriteLine($"StateChanged: {s}");
                };

                audioPlayer.PositionChanged += (p) =>
                {
                    Console.WriteLine($"PositionChanged: {p}");
                };

                audioPlayer.OnAudioDataAvailable += (pcmData) =>
                {
                    while (provider.BufferedBytes + pcmData.Length > provider.BufferLength)
                    {
                        Thread.Sleep(100);
                    }
                    provider.AddSamples(pcmData, 0, pcmData.Length);
                };

                //await audioPlayer.LoadAsync(@"./audioFile/Perfect.flac", sampleRate, channels, frameDurationMs);
                //audioPlayer.Play();

                using (var stream = File.OpenRead(@"./audioFile/Perfect.wav"))
                {
                    await audioPlayer.LoadAsync(stream, sampleRate, channels, frameDurationMs);

                    audioPlayer.Play(true);

                    Console.WriteLine("Play completed.");
                }
                Console.Read();
            }


        }
    }
}
