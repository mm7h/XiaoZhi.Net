using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Common.Models;

namespace XiaoZhi.Net.Server.Media.Subtitle
{
    /// <summary>
    /// 音频-字幕同步跟踪器：根据实际发送出的样本数精确对齐字幕
    /// </summary>
    internal sealed class AudioSubtitleSyncTracker : IAudioSubtitleSyncTracker
    {
        private readonly ILogger<AudioSubtitleSyncTracker> _logger;
        private readonly ConcurrentDictionary<AudioType, Queue<SubtitleTrackingInfo>> _pendingSubtitles;
        private readonly object _syncLock = new();
        private bool _disposed = false;

        public event Action<AudioType, string>? OnSubtitleStart;
        public event Action<AudioType, string>? OnSubtitleEnd;

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

        public void RegisterAudioSubtitle(AudioType audioType, string subtitleText,
            bool isFirstSegment, bool isLastSegment)
        {
            if (string.IsNullOrEmpty(subtitleText)) return;
            lock (_syncLock)
            {
                var queue = _pendingSubtitles.GetOrAdd(audioType, _ => new Queue<SubtitleTrackingInfo>());
                var trackingInfo = new SubtitleTrackingInfo
                {
                    AudioType = audioType,
                    SubtitleText = subtitleText,
                    IsFirstSegment = isFirstSegment,
                    IsLastSegment = isLastSegment,
                    RegisterTime = DateTime.UtcNow,
                    // sample-related fields set later if known
                };
                queue.Enqueue(trackingInfo);
            }
            _logger.LogDebug("Registered audio-subtitle: {SubtitleText}", subtitleText);
        }

        public void RegisterAudioSubtitle(AudioType audioType, string subtitleText, int sampleCount,
            bool isFirstSegment, bool isLastSegment)
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
                    IsFirstSegment = isFirstSegment,
                    IsLastSegment = isLastSegment,
                    RegisterTime = DateTime.UtcNow,
                    RemainingSamples = sampleCount,
                    TotalSamples = sampleCount
                };
                queue.Enqueue(trackingInfo);
            }
            _logger.LogDebug("Registered audio-subtitle with samples: {SubtitleText}, samples={Samples}", subtitleText, sampleCount);
        }

        public void AttachSamplesToNextSubtitle(AudioType audioType, int sampleCount)
        {
            if (sampleCount < 0) sampleCount = 0;
            lock (_syncLock)
            {
                if (_pendingSubtitles.TryGetValue(audioType, out var queue) && queue.Count > 0)
                {
                    var info = queue.Peek();
                    if (info.TotalSamples == 0)
                    {
                        info.TotalSamples = sampleCount;
                        info.RemainingSamples = sampleCount;
                        _logger.LogDebug("Attached samples to subtitle: {Subtitle}, samples={Samples}", info.SubtitleText, sampleCount);
                    }
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
                        OnSubtitleStart?.Invoke(audioType, info.SubtitleText);
                        _logger.LogDebug("Subtitle started for {AudioType}: {SubtitleText}", audioType, info.SubtitleText);
                    }

                    if (info.TotalSamples > 0)
                    {
                        int consume = Math.Min(info.RemainingSamples, samplesSent);
                        info.RemainingSamples -= consume;
                        samplesSent -= consume;
                    }
                    else
                    {
                        // 未提供样本数的字幕：采用“下一帧即完结”的保守策略
                        // 立即在首次样本到来时结束
                        samplesSent = 0;
                        info.RemainingSamples = 0;
                    }

                    if (info.RemainingSamples <= 0)
                    {
                        info.SubtitleEndSent = true;
                        info.IsAudioCompleted = true;
                        queue.Dequeue();
                        OnSubtitleEnd?.Invoke(audioType, info.SubtitleText);
                        _logger.LogDebug("Subtitle ended for {AudioType}: {SubtitleText}", audioType, info.SubtitleText);
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
                            OnSubtitleStart?.Invoke(audioType, info.SubtitleText);
                        }
                        if (!info.SubtitleEndSent)
                        {
                            info.SubtitleEndSent = true;
                            OnSubtitleEnd?.Invoke(audioType, info.SubtitleText);
                        }
                    }
                }
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
                            OnSubtitleEnd?.Invoke(audioType, trackingInfo.SubtitleText);
                        }
                    }
                    _logger.LogDebug("Cleared all subtitle tracking for {AudioType}", audioType);
                }
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
                            OnSubtitleEnd?.Invoke(trackingInfo.AudioType, trackingInfo.SubtitleText);
                        }
                    }
                }
                _pendingSubtitles.Clear();
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