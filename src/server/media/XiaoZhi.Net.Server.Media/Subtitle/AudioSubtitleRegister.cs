using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Dtos;

namespace XiaoZhi.Net.Server.Media.Subtitle
{
    /// <summary>
    /// 音频字幕注册器
    /// </summary>
    internal class AudioSubtitleRegister : IAudioSubtitleRegister
    {
        private readonly ILogger<AudioSubtitleRegister> _logger;
        private readonly ConcurrentDictionary<string, AudioSubtitle> _subtitlesCache;
        private bool _disposed = false;

        public AudioSubtitleRegister(ILogger<AudioSubtitleRegister>? logger = null)
        {
            this._logger = logger ?? NullLogger<AudioSubtitleRegister>.Instance;
            this._subtitlesCache = new ConcurrentDictionary<string, AudioSubtitle>();
        }

        public void Register(string sentenceId, AudioType audioType, TtsStatus ttsStatus, string subtitleText, Emotion emotion)
        {
            if (string.IsNullOrEmpty(subtitleText) || string.IsNullOrEmpty(sentenceId)) return;

            AudioSubtitle subtitleTrackingInfo = new AudioSubtitle(sentenceId, audioType, subtitleText, emotion, ttsStatus, DateTime.UtcNow);

            if (this._subtitlesCache.TryAdd(sentenceId, subtitleTrackingInfo))
            {
                this._logger.LogDebug("Registered subtitle: {SubtitleText}, Id: {Id}", subtitleText, sentenceId);
            }
            else
            {
                this._logger.LogWarning("Subtitle with Id: {Id} is already registered.", sentenceId);
            }
        }

        public bool GetSubtitle(string sentenceId, out AudioSubtitle subtitle)
        {
            return this._subtitlesCache.TryRemove(sentenceId, out subtitle);
        }

        public void ClearAll()
        {
            this._subtitlesCache.Clear();
            this._logger.LogDebug("Cleared all subtitle tracking data");
        }


        public void Dispose()
        {
            if (this._disposed) return;
            this._disposed = true;
            this.ClearAll();
            this._logger.LogDebug("AudioSubtitleRegister disposed");
        }
    }
}