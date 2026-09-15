using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Resources.AudioCaching
{
    /// <summary>
    /// Loads device-binding prompts through FFmpeg and caches normalized PCM samples.
    /// </summary>
    internal sealed class AudioFileCaching : BaseResource<AudioFileCaching, DeviceBindSetting>, IAudioFileCaching
    {
        public const string BindCodePromptKey = "BindCodePrompt";
        public const string BindNotFoundKey = "BindNotFound";
        private const int AudioCachingFrameDurationMilliseconds = 60;

        private readonly IDictionary<string, float[]> _audioDataCache =
            new ConcurrentDictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        private readonly XiaoZhiConfig _config;
        private readonly IStreamAudioPlayer _audioPlayer;

        public AudioFileCaching(XiaoZhiConfig config, IStreamAudioPlayer audioPlayer, ILogger<AudioFileCaching> logger) : base(logger)
        {
            this._config = config;
            this._audioPlayer = audioPlayer;
        }

        public override string ResourceName => nameof(AudioFileCaching);

        public override bool Load(DeviceBindSetting settings)
        {
            try
            {
                this._audioDataCache.Clear();

                string bindCodePromptFilePath = Path.Combine(Environment.CurrentDirectory, settings.BindCodePromptFilePath);
                if (!File.Exists(bindCodePromptFilePath))
                {
                    this.Logger.LogError(Lang.AudioFileCaching_Load_BindCodePromptNotExist, bindCodePromptFilePath);
                    return false;
                }
                if (!this.CacheAudioFile(BindCodePromptKey, bindCodePromptFilePath))
                {
                    return false;
                }

                string bindNotFoundFilePath = Path.Combine(Environment.CurrentDirectory, settings.BindNotFoundFilePath);
                if (!File.Exists(bindNotFoundFilePath))
                {
                    this.Logger.LogError(Lang.AudioFileCaching_Load_BindNotFoundNotExist, bindNotFoundFilePath);
                    return false;
                }
                if (!this.CacheAudioFile(BindNotFoundKey, bindNotFoundFilePath))
                {
                    return false;
                }

                string digitFolderPath = Path.Combine(Environment.CurrentDirectory, settings.BindCodeDigitFolderPath);
                string[] digitFiles = Directory.GetFiles(digitFolderPath);
                if (digitFiles.Length != 10)
                {
                    this.Logger.LogError(Lang.AudioFileCaching_Load_DigitFilesCountError);
                    return false;
                }

                HashSet<int> loadedDigits = new();
                foreach (string digitFile in digitFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(digitFile);
                    if (!int.TryParse(fileName, out int digit) || digit is < 0 or > 9 || !loadedDigits.Add(digit))
                    {
                        this.Logger.LogWarning(Lang.AudioFileCaching_Load_InvalidDigitFile, fileName);
                        return false;
                    }

                    if (!this.CacheAudioFile(digit.ToString(), digitFile))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                this._audioDataCache.Clear();
                this.Logger.LogError(ex, Lang.AudioFileCaching_Load_InvalidResourceLoading, this.ResourceName);
                return false;
            }
        }

        public bool TryGetAudioData(string cacheKey, out float[]? audioData)
        {
            if (!string.IsNullOrWhiteSpace(cacheKey)
                && this._audioDataCache.TryGetValue(cacheKey, out audioData)
                && audioData.Length > 0)
            {
                return true;
            }

            audioData = null;
            return false;
        }

        public override void Dispose()
        {
            this._audioDataCache.Clear();
        }

        private bool CacheAudioFile(string cacheKey, string filePath)
        {
            using FileStream audioStream = File.OpenRead(filePath);
            ArrayBufferWriter<float> cachedAudioWriter = new();

            void OnAudioData(float[] audioData, bool _, bool __)
            {
                audioData.AsSpan().CopyTo(cachedAudioWriter.GetSpan(audioData.Length));
                cachedAudioWriter.Advance(audioData.Length);
            }

            this._audioPlayer.OnAudioDataAvailable += OnAudioData;
            try
            {
                if (!this._audioPlayer.CheckFFmpegInstalledAsync().GetAwaiter().GetResult()
                    || !this._audioPlayer.LoadAsync(
                            audioStream,
                            this._config.AudioSetting.SampleRate,
                            this._config.AudioSetting.Channels,
                            AudioCachingFrameDurationMilliseconds)
                        .GetAwaiter()
                        .GetResult())
                {
                    this.Logger.LogWarning(Lang.AudioFileCaching_CacheAudioFile_DecodeFailed, filePath);
                    return false;
                }

                this._audioPlayer.DecodeAsync().GetAwaiter().GetResult();
                float[] cachedAudio = cachedAudioWriter.WrittenSpan.ToArray();
                if (cachedAudio.Length == 0)
                {
                    this.Logger.LogWarning(Lang.AudioFileCaching_CacheAudioFile_NoPcmData, filePath);
                    return false;
                }

                this._audioDataCache[cacheKey] = cachedAudio;
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.AudioFileCaching_CacheAudioFile_Failed, filePath);
                return false;
            }
            finally
            {
                this._audioPlayer.OnAudioDataAvailable -= OnAudioData;
            }
        }
    }
}
