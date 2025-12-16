using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Common.Models;

namespace XiaoZhi.Net.Server.Media.Subtitle
{
    /// <summary>
    /// ÒôÆµ-×ÖÄ»Í¬²½¸ú×ÙÆ÷
    /// </summary>
    internal sealed class AudioSubtitleSyncTracker : IAudioSubtitleSyncTracker
    {
        private readonly ILogger<AudioSubtitleSyncTracker> _logger;
        private readonly ConcurrentDictionary<AudioType, Queue<SubtitleTrackingInfo>> _pendingSubtitles;
        private readonly ConcurrentDictionary<AudioType, SubtitleTrackingInfo?> _currentProducing = new();
        private readonly object _syncLock = new();
        private bool _disposed = false;

        public event Action<AudioType, string, Emotion>? OnSubtitleStart;
        public event Action<AudioType, string, Emotion>? OnSubtitleEnd;

        public AudioSubtitleSyncTracker(ILogger<AudioSubtitleSyncTracker>? logger = null)
        {
            _pendingSubtitles = new ConcurrentDictionary<AudioType, Queue<SubtitleTrackingInfo>>
            {
                [AudioType.TTS] = new Queue<SubtitleTrackingInfo>(),
                [AudioType.Music] = new Queue<SubtitleTrackingInfo>(),
                [AudioType.SystemNotification] = new Queue<SubtitleTrackingInfo>(),
                [AudioType.Other] = new Queue<SubtitleTrackingInfo>()
            };
            _logger = logger ?? NullLogger<AudioSubtitleSyncTracker>.Instance;
        }

        public void RegisterAudioSubtitle(AudioType audioType, string subtitleText, Emotion emotion)
        {
            if (string.IsNullOrEmpty(subtitleText)) return;
            lock (_syncLock)
            {
                var queue = _pendingSubtitles.GetOrAdd(audioType, _ => new Queue<SubtitleTrackingInfo>());
                var trackingInfo = new SubtitleTrackingInfo
                {
                    AudioType = audioType,
                    SubtitleText = subtitleText,
                    RegisterTime = DateTime.UtcNow,
                    Emotion = emotion,
                    TotalSamples = 0,
                    RemainingSamples = 0
                };
                queue.Enqueue(trackingInfo);
                _currentProducing[audioType] = trackingInfo;
            }
            _logger.LogDebug("Registered audio-subtitle: {SubtitleText}", subtitleText);
        }

        public void RegisterAudioSubtitle(AudioType audioType, string subtitleText, int sampleCount, Emotion emotion)
        {
            if (string.IsNullOrEmpty(subtitleText)) return;
            if (sampleCount < 0) sampleCount = 0;
            lock (_syncLock)
            {
                var queue = _pendingSubtitles.GetOrAdd(audioType, _ => new Queue<SubtitleTrackingInfo>());
                var trackingInfo = new SubtitleTrackingInfo
                {
                    AudioType = audioType,
                    SubtitleText = subtitleText,
                    RegisterTime = DateTime.UtcNow,
                    Emotion = emotion,
                    RemainingSamples = sampleCount,
                    TotalSamples = sampleCount
                };
                queue.Enqueue(trackingInfo);
                _currentProducing[audioType] = trackingInfo;
            }
            _logger.LogDebug("Registered audio-subtitle with samples: {SubtitleText}, samples={Samples}", subtitleText, sampleCount);
        }

        public void AttachSamplesToNextSubtitle(AudioType audioType, int sampleCount)
        {
            if (sampleCount < 0) sampleCount = 0;
            if (sampleCount == 0) return;
            lock (_syncLock)
            {
                SubtitleTrackingInfo? target = null;
                if (_currentProducing.TryGetValue(audioType, out var producing) && producing is not null)
                {
                    target = producing;
                }
                else if (_pendingSubtitles.TryGetValue(audioType, out var queue) && queue.Count > 0)
                {
                    target = queue.Peek();
                }

                if (target is not null)
                {
                    target.TotalSamples += sampleCount;
                    target.RemainingSamples += sampleCount;
                    _logger.LogDebug("Accumulated samples to subtitle: {Subtitle}.", target.SubtitleText);
                }
            }
        }

        public void SealCurrentSubtitle(AudioType audioType)
        {
            lock (_syncLock)
            {
                if (_currentProducing.ContainsKey(audioType))
                {
                    _currentProducing[audioType] = null;
                    _logger.LogDebug("Sealed current producing subtitle for {AudioType}", audioType);
                }
            }
        }

        public void NotifyAudioSamplesSent(AudioType audioType, int samplesSent)
        {
            if (samplesSent <= 0) return;
            lock (_syncLock)
            {
                if (!_pendingSubtitles.TryGetValue(audioType, out var queue) || queue.Count == 0)
                {
                    return;
                }

                while (samplesSent > 0 && queue.Count > 0)
                {
                    var info = queue.Peek();

                    if (!info.SubtitleStartSent)
                    {
                        info.SubtitleStartSent = true;
                        info.IsAudioStarted = true;
                        OnSubtitleStart?.Invoke(audioType, info.SubtitleText, info.Emotion);
                        _logger.LogDebug("Subtitle started for {AudioType}: {SubtitleText}", audioType, info.SubtitleText);
                    }

                    if (info.TotalSamples > 0)
                    {
                        int consume = Math.Min(info.RemainingSamples, samplesSent);
                        info.RemainingSamples -= consume;
                        samplesSent -= consume;

                        if (info.RemainingSamples <= 0)
                        {
                            info.SubtitleEndSent = true;
                            info.IsAudioCompleted = true;
                            queue.Dequeue();
                            OnSubtitleEnd?.Invoke(audioType, info.SubtitleText, info.Emotion);
                            _logger.LogDebug("Subtitle ended for {AudioType}: {SubtitleText}", audioType, info.SubtitleText);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        public void NotifyAudioSendComplete(AudioType audioType)
        {
            lock (_syncLock)
            {
                if (_pendingSubtitles.TryGetValue(audioType, out var queue))
                {
                    while (queue.Count > 0)
                    {
                        var info = queue.Dequeue();
                        if (!info.SubtitleStartSent)
                        {
                            info.SubtitleStartSent = true;
                            OnSubtitleStart?.Invoke(audioType, info.SubtitleText, info.Emotion);
                        }
                        if (!info.SubtitleEndSent)
                        {
                            info.SubtitleEndSent = true;
                            OnSubtitleEnd?.Invoke(audioType, info.SubtitleText, info.Emotion);
                        }
                    }
                }
                _currentProducing[audioType] = null;
            }
        }

        public void ClearAudioType(AudioType audioType)
        {
            lock (_syncLock)
            {
                if (_pendingSubtitles.TryRemove(audioType, out var queue))
                {
                    while (queue.Count > 0)
                    {
                        var trackingInfo = queue.Dequeue();
                        if (trackingInfo.SubtitleStartSent && !trackingInfo.SubtitleEndSent)
                        {
                            OnSubtitleEnd?.Invoke(audioType, trackingInfo.SubtitleText, trackingInfo.Emotion);
                        }
                    }
                    _logger.LogDebug("Cleared all subtitle tracking for {AudioType}", audioType);
                }
                _currentProducing[audioType] = null;
            }
        }

        public void ClearAll()
        {
            lock (_syncLock)
            {
                foreach (var queue in _pendingSubtitles.Values)
                {
                    while (queue.Count > 0)
                    {
                        var trackingInfo = queue.Dequeue();
                        if (trackingInfo.SubtitleStartSent && !trackingInfo.SubtitleEndSent)
                        {
                            OnSubtitleEnd?.Invoke(trackingInfo.AudioType, trackingInfo.SubtitleText, trackingInfo.Emotion);
                        }
                    }
                }
                _pendingSubtitles.Clear();
                _currentProducing.Clear();
                _logger.LogDebug("Cleared all subtitle tracking data");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearAll();
            _logger.LogDebug("AudioSubtitleSyncTracker disposed");
        }
    }
}