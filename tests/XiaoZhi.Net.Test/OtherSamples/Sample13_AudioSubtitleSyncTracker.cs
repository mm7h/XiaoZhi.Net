using NAudio.Wave;
using SherpaOnnx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample13_AudioSubtitleSyncTracker
    {
        const string SYSTEM_AUDIO_FILE_PATH = "./audioFile/max_output_size.wav";
        const string MUSIC_AUDIO_FILE_PATH = "./audioFile/Perfect.flac";

        const int SAMPLE_RATE = 24000;
        const int CHANNELS = 1;
        const int FRAME_DURATION_MS = 60;

        const string MODEL_FILE_FOLER = "./models/kokoro";
        const float SPEAK_SPPED = 1.0f;
        const int SPERAKER_ID = 50;
        const string OUTPUT_TTS_WAV_FILE = "./models/output_tts.wav";

        /// <summary>
        /// 运行示例
        /// </summary>
        public static async Task Run()
        {
            float[] ttsAudio = GenerateTTSAudio();
            Console.WriteLine("TTS audio generated.");
            try
            {
                Console.WriteLine("Starting Enhanced AudioMixer Test with Smooth Volume Control...");

                MediaFactory.InitializeFFmpeg();

                using (var waveOut = new WaveOutEvent())
                {
                    // Reduced buffer size for lower latency
                    var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SAMPLE_RATE, CHANNELS));
                    provider.BufferLength = SAMPLE_RATE * 1 * CHANNELS * 4; // Reduced from 2 seconds to 1 second
                    waveOut.Init(provider);
                    waveOut.Play();

                    Console.WriteLine("Creating AudioMixer with smooth volume control...");

                    IAudioSubtitleSyncTracker subtitleTracker = MediaFactory.CreateAudioSubtitleSyncTracker();

                    subtitleTracker.OnSubtitleStart += (audioType, text) =>
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Subtitle Start - Type: {audioType}, Text: {text}");
                    };

                    subtitleTracker.OnSubtitleEnd += (audioType, text) =>
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Subtitle End - Type: {audioType}, Text: {text}");
                    };

                    IAudioMixer mixer = MediaFactory.CreateFFmpegAudioMixer(
                        SAMPLE_RATE,
                        CHANNELS,
                        FRAME_DURATION_MS,
                        new AudioMixerConfig()
                        {
                            VolumeTransitionDurationMs = 1000,
                            TransitionCurve = VolumeTransitionCurve.Logarithmic,
                            EnableSmoothVolumeControl = true
                        },
                        subtitleTracker
                    );
                    Console.WriteLine("Setting up event handlers...");
                    mixer.OnStateChanged += (state) =>
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Mixer state changed to: {state}");
                    };

                    var lastStatsTime = DateTime.Now;
                    mixer.OnMixingStatsUpdated += (stats) =>
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
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting TTS playback...");
                        subtitleTracker.RegisterAudioSubtitle(AudioType.TTS, "你好，欢迎使用小智AI助手1111!", ttsAudio.Length, true, true);
                        mixer.AddAudioData(AudioType.TTS, ttsAudio);
                        mixer.StopAudioStream(AudioType.TTS);
                    });

                    // Start another TTS after 35 seconds
                    Task ttsTask2 = Task.Run(async () =>
                    {
                        await Task.Delay(35 * 1000);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting second TTS playback...");
                        subtitleTracker.RegisterAudioSubtitle(AudioType.TTS, "你好，欢迎使用小智AI助手2222!", ttsAudio.Length, true, true);
                        mixer.AddAudioData(AudioType.TTS, ttsAudio);
                        mixer.StopAudioStream(AudioType.TTS);
                    });

                    // Start another TTS after 100 seconds
                    Task ttsTask3 = Task.Run(async () =>
                    {
                        await Task.Delay(100 * 1000);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting second TTS playback...");
                        subtitleTracker.RegisterAudioSubtitle(AudioType.TTS, "你好，欢迎使用小智AI助手3333!", ttsAudio.Length, true, true);
                        mixer.AddAudioData(AudioType.TTS, ttsAudio);
                        mixer.StopAudioStream(AudioType.TTS);
                    });

                    // Start system notification after 25 seconds (highest priority) - should suppress both smoothly
                    Task systemTask = Task.Run(async () =>
                    {
                        await Task.Delay(25 * 1000);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting System Notification playback (should smoothly suppress TTS and music over 500ms)...");
                        subtitleTracker.RegisterAudioSubtitle(AudioType.SystemNotification, "不好意思，明天这个时候再聊!", true, true);
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

        public static float[] GenerateTTSAudio()
        {
            var config = new OfflineTtsConfig();
            config.Model.Kokoro.Model = Path.Combine(MODEL_FILE_FOLER, "model.onnx");
            config.Model.Kokoro.Voices = Path.Combine(MODEL_FILE_FOLER, "voices.bin");
            config.Model.Kokoro.Tokens = Path.Combine(MODEL_FILE_FOLER, "tokens.txt");
            config.Model.Kokoro.DataDir = Path.Combine(MODEL_FILE_FOLER, "espeak-ng-data");
            config.Model.Kokoro.DictDir = Path.Combine(MODEL_FILE_FOLER, "dict");
            config.Model.Kokoro.Lexicon = Path.Combine(MODEL_FILE_FOLER, "./lexicon/lexicon-zh.txt") + "," + Path.Combine(MODEL_FILE_FOLER, "./lexicon/lexicon-us-en.txt");
            config.Model.NumThreads = 2;
            config.Model.Provider = "cpu";

            var tts = new OfflineTts(config);
            string text = "你好，欢迎使用小智AI助手!";

            OfflineTtsGeneratedAudio audio = tts.Generate(text, SPEAK_SPPED, SPERAKER_ID);

            if (File.Exists(OUTPUT_TTS_WAV_FILE))
            {
                File.Delete(OUTPUT_TTS_WAV_FILE);
            }
            audio.SaveToWaveFile(OUTPUT_TTS_WAV_FILE);

            return audio.Samples;
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
                    audioMixer.AddAudioData(audioType, pcmData);
                    framesSent++;

                    if (isFirst && !firstFrameSent)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {audioType}: First frame sent to mixer - volume transition should start");
                        firstFrameSent = true;
                    }

                    if (isLast && !lastFrameSent)
                    {
                        lastFrameSent = true;
                        // Important: inform the mixer that this stream is done so that volume can recover
                        audioMixer.StopAudioStream(audioType);
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

                // Safety: ensure we notify the mixer that this stream is done
                audioMixer.StopAudioStream(audioType);

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
