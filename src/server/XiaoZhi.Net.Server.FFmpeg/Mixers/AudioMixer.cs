using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.FFmpeg.Abstractions;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.FFmpeg.Mixers
{
    /// <summary>
    /// Audio mixer with advanced volume control and smooth transitions
    /// </summary>
    internal sealed class AudioMixer : IAudioMixer
    {
        private readonly ILogger<AudioMixer> _logger;
        private readonly object _syncLock = new();
        private readonly ConcurrentDictionary<AudioType, AudioStreamProcessor> _audioInputs = new();
        private readonly ConcurrentDictionary<AudioType, VolumeTransitionControl> _volumeStates = new();
        private readonly Timer _mixingTimer;
        private volatile bool _hasPendingData = false;
        private volatile int _processingFlag = 0;
        private AudioMixerConfig _config = new();

        // Audio format settings
        private int _outputSampleRate;
        private int _outputChannels;
        private int _frameDuration;
        private int _frameSampleCount;

        // State management
        private bool _initialized = false;
        private bool _disposed = false;
        private AudioMixerState _state = AudioMixerState.Idle;
        private AudioMixerStats _currentStats = new();

        // Enhanced volume control for priority-based mixing
        private Dictionary<AudioType, float>? _baseVolumeLevels;

        // More aggressive volume reduction factors
        private Dictionary<AudioType, float>? _prioritySuppressionLevels;

        public AudioMixer(ILogger<AudioMixer>? logger = null)
        {
            _logger = logger ?? NullLogger<AudioMixer>.Instance;

            _mixingTimer = new Timer(ProcessMixingCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public event Action<AudioMixerState>? StateChanged;
        public event Action<float[], bool, bool>? OnMixedAudioDataAvailable;
        public event Action<AudioMixerStats>? OnStatsUpdated;

        public bool IsInitialized => _initialized;
        public int OutputSampleRate => _outputSampleRate;
        public int OutputChannels => _outputChannels;
        public int FrameDuration => _frameDuration;

        public bool Initialize(int outputSampleRate, int outputChannels, int frameDuration, AudioMixerConfig? config = null)
        {
            lock (_syncLock)
            {
                try
                {
                    if (_initialized)
                    {
                        _logger.LogWarning("FFmpegAudioMixer is already initialized");
                        return true;
                    }

                    if (config is not null)
                    {
                        _config = config;
                    }

                    _baseVolumeLevels = new()
                    {
                        { AudioType.SystemNotification, _config.SystemNotificationVolumeConfig.BaseVolume },
                        { AudioType.TTS, _config.TTSVolumeConfig.BaseVolume },
                        { AudioType.Music, _config.MusicVolumeConfig.BaseVolume },
                        { AudioType.Other, 0.5f }
                    };

                    _prioritySuppressionLevels = new()
                    {
                        { AudioType.SystemNotification, _config.SystemNotificationVolumeConfig.SuppressionVolume },
                        { AudioType.TTS, _config.TTSVolumeConfig.SuppressionVolume },
                        { AudioType.Music, _config.MusicVolumeConfig.SuppressionVolume },
                        { AudioType.Other, 0.05f }
                    };

                    _outputSampleRate = outputSampleRate;
                    _outputChannels = outputChannels;
                    _frameDuration = frameDuration;
                    _frameSampleCount = outputSampleRate * frameDuration / 1000 * outputChannels;

                    // 使用更高频率的定时器来支持平滑的音量过渡
                    var timerInterval = Math.Max(frameDuration / 4, 5); // 至少5ms，支持更平滑的过渡
                    _mixingTimer.Change(timerInterval, timerInterval);

                    _initialized = true;
                    SetState(AudioMixerState.Idle);

                    _logger.LogInformation("FFmpegAudioMixer initialized: {SampleRate}Hz, {Channels} channels, {FrameDuration}ms frames, timer interval: {TimerInterval}ms",
                        outputSampleRate, outputChannels, frameDuration, timerInterval);

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize FFmpegAudioMixer");
                    return false;
                }
            }
        }

        public void AddAudioData(AudioType audioType, float[] audioData)
        {
            if (!_initialized || _disposed || audioData == null || audioData.Length == 0)
            {
                return;
            }

            _logger.LogDebug("Adding audio data for {AudioType} with {SampleCount} samples", audioType, audioData.Length);

            // Get or create input stream for this audio type
            var audioInput = _audioInputs.GetOrAdd(audioType,
                _ => new AudioStreamProcessor(audioType, _outputSampleRate, _outputChannels, _frameDuration, _config));

            var volumeState = _volumeStates.GetOrAdd(audioType, _ => new VolumeTransitionControl());

            // Add data to the input stream with automatic frame boundary detection
            audioInput.AddData(audioData);

            // Check if this is a new stream (first data)
            if (audioInput.ProcessedFrameCount == 0 && audioInput.IsFirstFrame)
            {
                UpdateVolumeTargets();
            }

            _hasPendingData = true;

            if (_state == AudioMixerState.Idle)
            {
                SetState(AudioMixerState.Mixing);
            }

            TryProcessMixingImmediate();
        }

        private void UpdateVolumeTargets()
        {
            if (!_config.EnableSmoothVolumeControl)
                return;

            var activeTypes = _audioInputs.Keys.ToList();
            if (activeTypes.Count == 0)
                return;

            var highestPriority = activeTypes.Max(t => (int)t);

            foreach (var audioType in activeTypes)
            {
                var volumeState = _volumeStates.GetOrAdd(audioType, _ => new VolumeTransitionControl());
                var targetVolume = CalculateTargetVolume(audioType, highestPriority, activeTypes);

                volumeState.StartTransition(targetVolume, _config.VolumeTransitionDurationMs, _config.TransitionCurve);

                _logger.LogDebug("Volume transition started for {AudioType}: {Current:F3} -> {Target:F3}",
                    audioType, volumeState.CurrentVolume, targetVolume);
            }
        }

        private float CalculateTargetVolume(AudioType audioType, int highestPriority, List<AudioType> activeTypes)
        {
            var baseVolume = _baseVolumeLevels?.GetValueOrDefault(audioType, 0.5f) ?? 0.5f;
            var currentPriority = (int)audioType;

            if (currentPriority == highestPriority)
            {
                return baseVolume;
            }

            // 直接使用配置的抑制音量值，而不是作为百分比计算
            var suppressionVolume = _prioritySuppressionLevels?.GetValueOrDefault(audioType, 0.05f) ?? 0.05f;
            var higherPriorityCount = activeTypes.Count(t => (int)t > currentPriority);

            if (higherPriorityCount > 0)
            {
                // 对于多个高优先级音频同时播放的情况，进一步降低音量
                suppressionVolume *= (float)Math.Pow(0.5, higherPriorityCount - 1);
            }

            return suppressionVolume;
        }

        private void TryProcessMixingImmediate()
        {
            if (Interlocked.CompareExchange(ref _processingFlag, 1, 0) == 0)
            {
                try
                {
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            ProcessMixing();
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _processingFlag, 0);
                        }
                    }, TaskCreationOptions.LongRunning);
                }
                catch
                {
                    Interlocked.Exchange(ref _processingFlag, 0);
                }
            }
        }

        private void ProcessMixingCallback(object? state)
        {
            if (!_initialized || _disposed)
                return;

            // 即使没有新数据，也要处理音量过渡
            bool hasActiveTransitions = _volumeStates.Values.Any(v => v.IsTransitioning);
            if (!_hasPendingData && !hasActiveTransitions)
                return;

            if (Interlocked.CompareExchange(ref _processingFlag, 1, 0) == 0)
            {
                try
                {
                    ProcessMixing();
                }
                finally
                {
                    Interlocked.Exchange(ref _processingFlag, 0);
                }
            }
        }

        private void ProcessMixing()
        {
            if (_audioInputs.IsEmpty)
            {
                _hasPendingData = false;
                return;
            }

            try
            {
                bool hasProcessedData = false;
                int processedFrameCount = 0;
                const int maxFramesPerCycle = 3; // 减少每次处理的帧数以提高响应性

                while (processedFrameCount < maxFramesPerCycle)
                {
                    var allInputs = _audioInputs.Values.ToList();
                    var inputsWithData = allInputs.Where(input => input.HasAnyData() && !input.IsComplete).ToList();

                    if (inputsWithData.Count == 0)
                    {
                        // 检查是否有音量过渡需要处理
                        bool hasActiveTransitions = _volumeStates.Values.Any(v => v.IsTransitioning);
                        if (hasActiveTransitions && allInputs.Count > 0)
                        {
                            // 为了保持音量过渡的连续性，生成静音帧
                            var silentFrame = new float[_frameSampleCount];
                            OnMixedAudioDataAvailable?.Invoke(silentFrame, false, false);
                            hasProcessedData = true;
                            processedFrameCount++;
                            continue;
                        }
                        break;
                    }

                    // 使用改进的缓冲区策略
                    var activeInputs = GetActiveInputsWithBufferStrategy(inputsWithData);
                    if (activeInputs.Count == 0)
                    {
                        break;
                    }

                    var currentActiveTypes = activeInputs.Select(input => input.AudioType).ToList();

                    // 更新音量目标（如果有新的活跃类型）
                    if (currentActiveTypes.Count != _volumeStates.Count ||
                        currentActiveTypes.Any(t => !_volumeStates.ContainsKey(t)))
                    {
                        UpdateVolumeTargets();
                    }

                    var mixedAudio = MixAudioStreamsWithSmoothVolume(activeInputs, currentActiveTypes);

                    if (mixedAudio != null && mixedAudio.Length > 0)
                    {
                        ApplyEnhancedFadeEffects(mixedAudio, activeInputs);
                        ApplyEnhancedLimiting(mixedAudio);
                        ApplyDynamicGainControlSmooth(mixedAudio, activeInputs.Count > 1);
                        UpdateStatistics(mixedAudio, activeInputs.Count);

                        bool isFirst = activeInputs.Any(input => input.IsFirstFrame);
                        bool isLast = activeInputs.Any(input => input.IsLastFrame) &&
                                     activeInputs.All(input => input.IsComplete || !input.HasAnyData());

                        OnMixedAudioDataAvailable?.Invoke(mixedAudio, isFirst, isLast);

                        foreach (var input in activeInputs)
                        {
                            input.MarkFrameProcessed();
                        }

                        hasProcessedData = true;
                        processedFrameCount++;
                    }
                    else
                    {
                        break;
                    }
                }

                // 清理完成的流并触发音量恢复
                var completedStreams = _audioInputs.Where(kvp => kvp.Value.IsComplete && !kvp.Value.HasAnyData()).ToList();
                bool hasCompletedStreams = completedStreams.Count > 0;

                foreach (var completedStream in completedStreams)
                {
                    if (_audioInputs.TryRemove(completedStream.Key, out var stream))
                    {
                        stream.Dispose();
                        _volumeStates.TryRemove(completedStream.Key, out _); // 同时清理音量状态
                    }
                }

                // 如果有流完成，重新计算剩余流的音量目标
                if (hasCompletedStreams && _audioInputs.Count > 0)
                {
                    _logger.LogDebug("Audio stream completed, recalculating volume targets for remaining streams");
                    UpdateVolumeTargets(); // 使用现有的方法
                    _hasPendingData = true; // 确保继续处理音量过渡
                }

                // 更新状态
                if (_audioInputs.IsEmpty)
                {
                    SetState(AudioMixerState.Idle);
                    _hasPendingData = false;
                }
                else if (!hasProcessedData)
                {
                    var hasAnyData = _audioInputs.Values.Any(input => input.HasAnyData());
                    var hasActiveTransitions = _volumeStates.Values.Any(v => v.IsTransitioning);
                    if (!hasAnyData && !hasActiveTransitions)
                    {
                        _hasPendingData = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during audio mixing");
                _hasPendingData = false;
            }
        }

        private List<AudioStreamProcessor> GetActiveInputsWithBufferStrategy(List<AudioStreamProcessor> inputsWithData)
        {
            var activeInputs = new List<AudioStreamProcessor>();

            foreach (var input in inputsWithData)
            {
                bool hasFullFrame = input.HasDataForFrame(_frameSampleCount);
                bool isNewStream = input.ProcessedFrameCount < 3;

                if (hasFullFrame)
                {
                    activeInputs.Add(input);
                }
                else if (isNewStream && _config.EnableSmoothVolumeControl)
                {
                    // 对于新流，使用配置的容忍度
                    int minRequiredSamples = (int)(_frameSampleCount * _config.NewStreamBufferTolerance);
                    if (input.AvailableDataCount >= minRequiredSamples)
                    {
                        activeInputs.Add(input);
                    }
                }
            }

            return activeInputs;
        }

        private float[]? MixAudioStreamsWithSmoothVolume(List<AudioStreamProcessor> activeInputs, List<AudioType> currentActiveTypes)
        {
            if (activeInputs.Count == 0)
            {
                return null;
            }

            var mixedAudio = new float[_frameSampleCount];
            var streamCount = 0;

            foreach (var input in activeInputs)
            {
                var frameData = input.GetFrameDataWithPartialSupport(_frameSampleCount);
                if (frameData != null && frameData.Length > 0)
                {
                    var volumeState = _volumeStates.GetOrAdd(input.AudioType, _ => new VolumeTransitionControl());
                    var currentVolume = volumeState.UpdateAndGetCurrentVolume();

                    for (int i = 0; i < Math.Min(mixedAudio.Length, frameData.Length); i++)
                    {
                        mixedAudio[i] += frameData[i] * currentVolume;
                    }
                    streamCount++;
                }
            }

            if (streamCount > 1)
            {
                var normalizationFactor = (float)(0.75 / Math.Sqrt(streamCount));
                for (int i = 0; i < mixedAudio.Length; i++)
                {
                    mixedAudio[i] *= normalizationFactor;
                }
            }

            return mixedAudio;
        }

        private void ApplyEnhancedFadeEffects(float[] audioData, List<AudioStreamProcessor> activeInputs)
        {
            const int fadeLength = 16;

            foreach (var input in activeInputs)
            {
                if (input.IsFirstFrame)
                {
                    for (int i = 0; i < Math.Min(fadeLength, audioData.Length); i++)
                    {
                        float fadeGain = (float)i / fadeLength;
                        audioData[i] *= fadeGain;
                    }
                }

                if (input.IsLastFrame)
                {
                    int startIndex = Math.Max(0, audioData.Length - fadeLength);
                    for (int i = startIndex; i < audioData.Length; i++)
                    {
                        float fadeGain = 1.0f - (float)(i - startIndex) / fadeLength;
                        audioData[i] *= fadeGain;
                    }
                }
            }
        }

        private void ApplyEnhancedLimiting(float[] audioData)
        {
            const float threshold = 0.85f;
            const float ratio = 8.0f;

            for (int i = 0; i < audioData.Length; i++)
            {
                float absLevel = Math.Abs(audioData[i]);
                if (absLevel > threshold)
                {
                    float excess = absLevel - threshold;
                    float compressedExcess = excess / ratio;
                    float newLevel = threshold + compressedExcess;

                    audioData[i] = Math.Sign(audioData[i]) * Math.Min(newLevel, 0.9f);
                    _currentStats.LimiterTriggerCount++;
                }
            }
        }

        private void ApplyDynamicGainControlSmooth(float[] audioData, bool isMultiStream)
        {
            float rmsSum = 0;
            for (int i = 0; i < audioData.Length; i++)
            {
                rmsSum += audioData[i] * audioData[i];
            }
            float rms = (float)Math.Sqrt(rmsSum / audioData.Length);

            float targetLevel = isMultiStream ? 0.4f : 0.5f;

            if (rms > 0.005f)
            {
                float gain = Math.Min(targetLevel / rms, 1.1f);
                gain = Math.Max(gain, 0.4f);

                float threshold = isMultiStream ? 0.05f : 0.1f;
                if (Math.Abs(gain - 1.0f) > threshold)
                {
                    float smoothedGain = 1.0f + (gain - 1.0f) * 0.3f;
                    for (int i = 0; i < audioData.Length; i++)
                    {
                        audioData[i] *= smoothedGain;
                    }
                }
            }
        }

        private void UpdateStatistics(float[] audioData, int activeStreamCount)
        {
            float sumSquares = 0;
            float peak = 0;

            for (int i = 0; i < audioData.Length; i++)
            {
                float sample = Math.Abs(audioData[i]);
                sumSquares += audioData[i] * audioData[i];
                if (sample > peak)
                {
                    peak = sample;
                }
            }

            _currentStats.CurrentRms = (float)Math.Sqrt(sumSquares / audioData.Length);
            _currentStats.CurrentPeak = peak;
            _currentStats.CurrentGainDb = 20 * (float)Math.Log10(Math.Max(_currentStats.CurrentRms, 1e-10f));
            _currentStats.ActiveStreamCount = activeStreamCount;

            OnStatsUpdated?.Invoke(_currentStats);
        }

        public void StopAudioStream(AudioType audioType)
        {
            if (_audioInputs.TryGetValue(audioType, out var audioInput))
            {
                audioInput.Stop();
                _logger.LogDebug("Stopped audio stream for {AudioType}", audioType);
                _hasPendingData = true;
            }
        }

        public void ClearAllBuffers()
        {
            lock (_syncLock)
            {
                foreach (var input in _audioInputs.Values)
                {
                    input.ClearBuffer();
                }
                _volumeStates.Clear();
                SetState(AudioMixerState.Idle);
                _hasPendingData = false;
                _logger.LogDebug("Cleared all audio buffers");
            }
        }

        public AudioMixerStats GetCurrentStats()
        {
            return new AudioMixerStats
            {
                CurrentRms = _currentStats.CurrentRms,
                CurrentPeak = _currentStats.CurrentPeak,
                CurrentGainDb = _currentStats.CurrentGainDb,
                LimiterTriggerCount = _currentStats.LimiterTriggerCount,
                ActiveStreamCount = _audioInputs.Count,
                DelayCompensation = new Dictionary<AudioType, float>()
            };
        }

        private void SetState(AudioMixerState newState)
        {
            if (_state != newState)
            {
                _state = newState;
                StateChanged?.Invoke(_state);
                _logger.LogDebug("FFmpegAudioMixer state changed to {State}", _state);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_syncLock)
            {
                SetState(AudioMixerState.Stopped);

                _mixingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _mixingTimer?.Dispose();

                SpinWait.SpinUntil(() => _processingFlag == 0, 1000);

                foreach (var input in _audioInputs.Values)
                {
                    input.Dispose();
                }
                _audioInputs.Clear();
                _volumeStates.Clear();

                _disposed = true;
                _initialized = false;
                _logger.LogInformation("FFmpegAudioMixer disposed");
            }
        }
    }
}