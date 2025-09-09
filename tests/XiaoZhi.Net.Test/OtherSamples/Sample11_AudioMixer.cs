using NAudio.Wave;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.FFmpeg;
using XiaoZhi.Net.Server.FFmpeg.Abstractions;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample11_AudioMixer
    {
        const string SYSTEM_AUDIO_FILE_PATH = "./audioFile/max_output_size.mp3";
        const string TTS_AUDIO_FILE_PATH = "./audioFile/bind_code.wav";
        const string MUSIC_AUDIO_FILE_PATH = "./audioFile/Perfect.mp3";

        const int SAMPLE_RATE = 16000;
        const int CHANNELS = 1;
        const int FRAME_DURATION_MS = 60;
        /// <summary>
        /// 运行示例
        /// </summary>
        public static async Task Run()
        {
            try
            {
                Console.WriteLine("Starting Enhanced AudioMixer Test with Smooth Volume Control...");

                if (!MediaFactory.InitializeFFmpeg())
                {
                    Console.WriteLine("Failed to initialize the ffmpeg.");
                    return;
                }

                using (var waveOut = new WaveOutEvent())
                {
                    // Reduced buffer size for lower latency
                    var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SAMPLE_RATE, CHANNELS));
                    provider.BufferLength = SAMPLE_RATE * 1 * CHANNELS * 4; // Reduced from 2 seconds to 1 second
                    waveOut.Init(provider);
                    waveOut.Play();

                    Console.WriteLine("Creating AudioMixer with smooth volume control...");
                    
                    // 创建带有平滑音量控制的音频混音器
                    // 配置：1000ms过渡时间，对数曲线，启用平滑控制
                    IAudioMixer mixer = MediaFactory.CreateAudioMixer(
                        SAMPLE_RATE, 
                        CHANNELS, 
                        FRAME_DURATION_MS,
                        new AudioMixerConfig() 
                        {
                            VolumeTransitionDurationMs = 1000,
                            TransitionCurve = VolumeTransitionCurve.Logarithmic,
                            EnableSmoothVolumeControl = true
                        }
                    );

                    Console.WriteLine("Setting up event handlers...");
                    mixer.StateChanged += (state) =>
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Mixer state changed to: {state}");
                    };

                    var lastStatsTime = DateTime.Now;
                    mixer.OnStatsUpdated += (stats) =>
                    {
                        var now = DateTime.Now;
                        if ((now - lastStatsTime).TotalMilliseconds > 1000) // Log stats every second
                        {
                            Console.WriteLine($"[{now:HH:mm:ss.fff}] Stats - RMS: {stats.CurrentRms:F3}, Peak: {stats.CurrentPeak:F3}, Active: {stats.ActiveStreamCount}, Limiter: {stats.LimiterTriggerCount}");
                            lastStatsTime = now;
                        }
                    };

                    int frameCount = 0;
                    mixer.OnMixedAudioDataAvailable += (pcmData, isFirst, isLast) =>
                    {
                        var byteData = new byte[pcmData.Length * 4];
                        Buffer.BlockCopy(pcmData, 0, byteData, 0, byteData.Length);

                        // Reduced waiting time for lower latency
                        while (provider.BufferedBytes + byteData.Length > provider.BufferLength)
                        {
                            Thread.Sleep(5); // Further reduced to 5ms for better responsiveness
                        }
                        provider.AddSamples(byteData, 0, byteData.Length);

                        frameCount++;

                        if (isFirst)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] *** First mixed audio frame received (Frame #{frameCount})");
                        }
                        if (isLast)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] *** Last mixed audio frame received (Frame #{frameCount})");
                        }

                        // Log frame processing for debugging
                        if (frameCount % 100 == 0)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Processed {frameCount} mixed frames");
                        }
                    };

                    Console.WriteLine("Starting audio decoding tasks with staggered timing...");
                    Console.WriteLine("音量变化说明:");
                    Console.WriteLine("- Music: 基础音量 60%");
                    Console.WriteLine("- TTS: 基础音量 80%, 启动时Music降至6% (500ms对数过渡)");
                    Console.WriteLine("- SystemNotification: 基础音量 100%, 启动时其他音频降至更低音量 (500ms对数过渡)");
                    Console.WriteLine();

                    // Start music immediately (lowest priority)
                    Task musicTask = Task.Run(async () =>
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting Music playback...");
                        await DecodeAudio(MUSIC_AUDIO_FILE_PATH, AudioType.Music, mixer);
                    });

                    // Start TTS after 15 seconds (medium priority) - should suppress music smoothly
                    Task ttsTask = Task.Run(async () =>
                    {
                        await Task.Delay(15 * 1000);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting TTS playback (should smoothly suppress music over 500ms)...");
                        await DecodeAudio(TTS_AUDIO_FILE_PATH, AudioType.TTS, mixer);
                    });

                    // Start another TTS after 35 seconds
                    Task ttsTask2 = Task.Run(async () =>
                    {
                        await Task.Delay(35 * 1000);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting second TTS playback...");
                        await DecodeAudio(TTS_AUDIO_FILE_PATH, AudioType.TTS, mixer);
                    });

                    // Start another TTS after 100 seconds
                    Task ttsTask3 = Task.Run(async () =>
                    {
                        await Task.Delay(100 * 1000);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting second TTS playback...");
                        await DecodeAudio(TTS_AUDIO_FILE_PATH, AudioType.TTS, mixer);
                    });

                    // Start system notification after 25 seconds (highest priority) - should suppress both smoothly
                    Task systemTask = Task.Run(async () =>
                    {
                        await Task.Delay(25 * 1000);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting System Notification playback (should smoothly suppress TTS and music over 500ms)...");
                        await DecodeAudio(SYSTEM_AUDIO_FILE_PATH, AudioType.SystemNotification, mixer);
                    });

                    Console.WriteLine("Waiting for all tasks to complete...");
                    await Task.WhenAll(systemTask, ttsTask, musicTask, ttsTask2, ttsTask3);

                    Console.WriteLine("All audio tasks completed. Final stats:");
                    var finalStats = mixer.GetCurrentStats();
                    Console.WriteLine($"Final - RMS: {finalStats.CurrentRms:F3}, Peak: {finalStats.CurrentPeak:F3}, Limiter triggers: {finalStats.LimiterTriggerCount}");
                    Console.WriteLine($"Total frames processed: {frameCount}");

                    // Wait a bit more for any remaining audio to play
                    await Task.Delay(3000);

                    mixer.Dispose();
                    Console.WriteLine("AudioMixer disposed.");
                }

                Console.WriteLine("Enhanced AudioMixer test with smooth volume control completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got the error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private static async Task DecodeAudio(string filePath, AudioType audioType, IAudioMixer audioMixer)
        {
            try
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting {audioType} decoder for: {Path.GetFileName(filePath)}");

                IUrlAudioPlayer audioPlayer = MediaFactory.CreateUrlAudioPlayer();
                if (!audioPlayer.CheckFFmpegInstalled())
                {
                    Console.WriteLine("Failed to initialize the ffmpeg.");
                    return;
                }

                int framesSent = 0;
                bool firstFrameSent = false;
                bool lastFrameSent = false;

                audioPlayer.OnAudioDataAvailable += (pcmData, isFirst, isLast) =>
                {
                    audioMixer.AddAudioData(audioType, pcmData, isFirst, isLast);
                    framesSent++;

                    if (isFirst && !firstFrameSent)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {audioType}: First frame sent to mixer - volume transition should start");
                        firstFrameSent = true;
                    }

                    if (isLast && !lastFrameSent)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {audioType}: Last frame sent to mixer (Total frames: {framesSent}) - volume should transition back for remaining streams");
                        lastFrameSent = true;
                    }

                    // Log progress every 50 frames
                    if (framesSent % 50 == 0)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {audioType}: Sent {framesSent} frames to mixer");
                    }
                };

                audioPlayer.StateChanged += (state) =>
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {audioType} player state: {state}");
                };

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {audioType}: Loading audio file...");
                await audioPlayer.LoadAsync(filePath, SAMPLE_RATE, CHANNELS, FRAME_DURATION_MS);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {audioType}: Starting playback...");
                audioPlayer.Play(true); // Blocking playback

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {audioType}: Playback completed. Total frames sent: {framesSent}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Expected: Other audio streams should now recover their volume over 500ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Error in {audioType} decoder: {ex.Message}");
            }
        }
    }
}
