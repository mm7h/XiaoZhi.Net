using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.FFmpeg.Abstractions;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.FFmpeg.Mixers
{
    /// <summary>
    /// 基于FFmpeg的原生音频混音器实现
    /// </summary>
    internal sealed unsafe class FFmpegAudioMixer : IAudioMixer
    {
        private readonly ILogger<FFmpegAudioMixer> _logger;
        private readonly ConcurrentDictionary<AudioType, AudioStreamProcessor> _audioStreams;
        private readonly ConcurrentDictionary<AudioType, VolumeTransitionControl> _volumeStates;
        private readonly Dictionary<AudioType, float> _volumeLevels;
        private readonly Dictionary<AudioType, int> _priorities;
        private readonly Timer _processingTimer;

        private AVFilterGraph* _filterGraph;
        private AVFilterContext* _amixFilterCtx;
        private AVFilterContext* _sinkFilterCtx;
        private readonly Dictionary<AudioType, IntPtr> _sourceFilters;

        private AudioMixerConfig _config = new();
        private bool _initialized;
        private readonly object _filterLock = new();
        private bool _disposed;
        private volatile bool _hasPendingData = false;
        private volatile int _processingFlag = 0;

        // Audio format settings
        private int _outputSampleRate;
        private int _outputChannels;
        private int _frameDuration;
        private int _frameSampleCount;

        // State management
        private AudioMixerState _state = AudioMixerState.Idle;
        private AudioMixerStats _currentStats = new();

        // Enhanced volume control for priority-based mixing
        private Dictionary<AudioType, float>? _baseVolumeLevels;
        private Dictionary<AudioType, float>? _prioritySuppressionLevels;

        // Normalization smoothing (avoid first-frame stutter when new low-energy stream joins)
        private float _lastNormalizationFactor = 0.75f;

        public FFmpegAudioMixer(ILogger<FFmpegAudioMixer> logger)
        {
            _logger = logger;
            _audioStreams = new ConcurrentDictionary<AudioType, AudioStreamProcessor>();
            _volumeStates = new ConcurrentDictionary<AudioType, VolumeTransitionControl>();
            _volumeLevels = new Dictionary<AudioType, float>();
            _priorities = new Dictionary<AudioType, int>();
            _sourceFilters = new Dictionary<AudioType, IntPtr>();

            // 创建处理定时器
            _processingTimer = new Timer(ProcessMixingCallback, null, Timeout.Infinite, Timeout.Infinite);

            InitializeAudioTypes();
        }

        // Events
        public event Action<AudioMixerState>? StateChanged;
        public event Action<float[], bool, bool>? OnMixedAudioDataAvailable;
        public event Action<AudioMixerStats>? OnStatsUpdated;

        // Properties
        public bool IsInitialized => _initialized;
        public int OutputSampleRate => _outputSampleRate;
        public int OutputChannels => _outputChannels;
        public int FrameDuration => _frameDuration;

        public bool Initialize(int outputSampleRate, int outputChannels, int frameDuration, AudioMixerConfig? config = null)
        {
            lock (_filterLock)
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

                    _outputSampleRate = outputSampleRate;
                    _outputChannels = outputChannels;
                    _frameDuration = frameDuration;
                    _frameSampleCount = outputSampleRate * frameDuration / 1000 * outputChannels;

                    // Initialize volume levels from config
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

                    // Update volume levels based on config
                    UpdateVolumeLevelsFromConfig();
                    
                    // 启动处理定时器 - 使用更高频率来支持平滑的音量过渡
                    var timerInterval = Math.Max(frameDuration / 4, 5); // 至少5ms，支持更平滑的过渡
                    _processingTimer.Change(timerInterval, timerInterval);

                    _initialized = true;
                    SetState(AudioMixerState.Idle);

                    _logger.LogInformation("FFmpeg audio mixer initialized successfully with SampleRate={SampleRate}, Channels={Channels}, FrameDuration={FrameDuration}ms, timer interval: {TimerInterval}ms",
                        outputSampleRate, outputChannels, frameDuration, timerInterval);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize FFmpeg audio mixer");
                    return false;
                }
            }
        }

        private void UpdateVolumeLevelsFromConfig()
        {
            _volumeLevels[AudioType.SystemNotification] = _config.SystemNotificationVolumeConfig.BaseVolume;
            _volumeLevels[AudioType.TTS] = _config.TTSVolumeConfig.BaseVolume;
            _volumeLevels[AudioType.Music] = _config.MusicVolumeConfig.BaseVolume;
        }

        private void InitializeAudioTypes()
        {
            var audioTypes = new[] { AudioType.TTS, AudioType.Music, AudioType.SystemNotification };

            foreach (var audioType in audioTypes)
            {
                switch (audioType)
                {
                    case AudioType.SystemNotification:
                        _volumeLevels[audioType] = 1.0f;
                        _priorities[audioType] = 1;
                        break;
                    case AudioType.TTS:
                        _volumeLevels[audioType] = 0.9f;
                        _priorities[audioType] = 2;
                        break;
                    case AudioType.Music:
                        _volumeLevels[audioType] = 0.3f;
                        _priorities[audioType] = 3;
                        break;
                    default:
                        _volumeLevels[audioType] = 0.5f;
                        _priorities[audioType] = 4;
                        break;
                }
            }
        }

        public void AddAudioData(AudioType audioType, float[] audioData)
        {
            if (!_initialized || _disposed)
                return;

            // 按需创建音频流处理器，而不是预创建
            var processor = _audioStreams.GetOrAdd(audioType, 
                _ => new AudioStreamProcessor(audioType, _outputSampleRate, _outputChannels, _frameDuration, _config));

            // 获取或创建音量状态管理器
            var volumeState = _volumeStates.GetOrAdd(audioType, _ => new VolumeTransitionControl());

            // 使用 AudioStreamProcessor 的 AddData 方法
            processor.AddData(audioData);

            // 检查是否是新流的第一帧数据
            if (processor.ProcessedFrameCount == 0 && processor.IsFirstFrame)
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

            var activeTypes = _audioStreams.Keys
                .Where(key => _audioStreams[key].HasAnyData() && !_audioStreams[key].IsComplete)
                .ToList();

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

            var suppressionVolume = _prioritySuppressionLevels?.GetValueOrDefault(audioType, 0.05f) ?? 0.05f;
            var higherPriorityCount = activeTypes.Count(t => (int)t > currentPriority);

            if (higherPriorityCount > 0)
            {
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
            if (!_initialized || _disposed)
                return;

            try
            {
                bool hasProcessedData = false;
                int processedFrameCount = 0;
                const int maxFramesPerCycle = 3; // 减少每次处理的帧数以提高响应性

                while (processedFrameCount < maxFramesPerCycle)
                {
                    var allInputs = _audioStreams.Values.ToList();
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

                    var activeInputs = GetActiveInputsWithBufferStrategy(inputsWithData);
                    if (activeInputs.Count == 0)
                    {
                        break;
                    }

                    var currentActiveTypes = activeInputs.Select(input => input.AudioType).ToList();

                    if (currentActiveTypes.Count != _volumeStates.Count ||
                        currentActiveTypes.Any(t => !_volumeStates.ContainsKey(t)))
                    {
                        UpdateVolumeTargets();
                    }

                    var mixedData = MixAudioStreamsWithSmoothVolume(activeInputs, currentActiveTypes);
                    if (mixedData != null && mixedData.Length > 0)
                    {
                        ApplyEnhancedLimiting(mixedData);
                        ApplyDynamicGainControlSmooth(mixedData, activeInputs.Count > 1);

                        bool isFirst = activeInputs.Any(input => input.IsFirstFrame);
                        bool isLast = activeInputs.Any(input => input.IsLastFrame) &&
                                     activeInputs.All(input => input.IsComplete || !input.HasAnyData());

                        // 更新统计信息
                        UpdateStatistics(mixedData, activeInputs.Count);

                        // 触发混合音频数据可用事件
                        OnMixedAudioDataAvailable?.Invoke(mixedData, isFirst, isLast);

                        // 标记帧已处理
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

                // 清理已完成的流并触发音量恢复
                var completedStreams = _audioStreams.Where(kvp => kvp.Value.IsComplete && !kvp.Value.HasAnyData()).ToList();
                bool hasCompletedStreams = completedStreams.Count > 0;

                foreach (var completedStream in completedStreams)
                {
                    if (_audioStreams.TryRemove(completedStream.Key, out var processor))
                    {
                        processor.Dispose();
                        _volumeStates.TryRemove(completedStream.Key, out _); // 清理音量状态
                        _logger.LogDebug("Removed completed audio stream for {AudioType}", completedStream.Key);
                    }
                }

                // 如果有流完成，重新计算剩余流的音量目标
                if (hasCompletedStreams && _audioStreams.Count > 0)
                {
                    _logger.LogDebug("Audio stream completed, recalculating volume targets for remaining streams");
                    UpdateVolumeTargets();
                    _hasPendingData = true; // 确保继续处理音量过渡
                }

                if (_audioStreams.IsEmpty)
                {
                    SetState(AudioMixerState.Idle);
                    _hasPendingData = false;
                }
                else if (!hasProcessedData)
                {
                    var hasAnyData = _audioStreams.Values.Any(p => p.HasAnyData());
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
                    int minRequiredSamples = (int)(_frameSampleCount * _config.NewStreamBufferTolerance);
                    if (input.AvailableDataCount >= minRequiredSamples)
                    {
                        activeInputs.Add(input);
                    }
                }
                else if (input.IsStopping && input.AvailableDataCount > 0)
                {
                    activeInputs.Add(input);
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

            var mixed = new float[_frameSampleCount];
            var energies = new List<(AudioStreamProcessor stream, float energy, bool warmup)>();

            foreach (var input in activeInputs)
            {
                var frameData = input.GetFrameDataWithPartialSupport(_frameSampleCount);
                if (frameData == null || frameData.Length == 0)
                    continue;

                const int fadeLength = 16;
                if (input.IsFirstFrame)
                {
                    int len = Math.Min(fadeLength, frameData.Length);
                    for (int i = 0; i < len; i++)
                    {
                        frameData[i] *= (float)i / len;
                    }
                }
                if (input.IsLastFrame)
                {
                    int len = Math.Min(fadeLength, frameData.Length);
                    int start = frameData.Length - len; if (start < 0) start = 0;
                    for (int i = start; i < frameData.Length; i++)
                    {
                        float g = 1f - (float)(i - start) / len;
                        frameData[i] *= g;
                    }
                }

                var volumeState = _volumeStates.GetOrAdd(input.AudioType, _ => new VolumeTransitionControl());
                var currentVolume = volumeState.UpdateAndGetCurrentVolume();
                float absSum = 0f;
                int lenAll = Math.Min(mixed.Length, frameData.Length);
                for (int i = 0; i < lenAll; i++)
                {
                    float s = frameData[i] * currentVolume;
                    mixed[i] += s;
                    absSum += Math.Abs(s);
                }
                float avgAbs = absSum / Math.Max(1, lenAll);
                bool warmup = input.ProcessedFrameCount == 0; // first processed frame
                energies.Add((input, avgAbs, warmup));
            }

            if (energies.Count <= 1)
            {
                _lastNormalizationFactor = SmoothNormalization(_lastNormalizationFactor, 0.75f);
                for (int i = 0; i < mixed.Length; i++)
                {
                    mixed[i] *= _lastNormalizationFactor;
                }
                return mixed;
            }

            const float energyThreshold = 0.003f;
            var effective = energies.Where(e => !e.warmup && e.energy >= energyThreshold).ToList();
            int effectiveCount = effective.Count == 0 ? 1 : effective.Count;
            float targetNorm = (float)(0.75 / Math.Sqrt(effectiveCount));
            _lastNormalizationFactor = SmoothNormalization(_lastNormalizationFactor, targetNorm);
            for (int i = 0; i < mixed.Length; i++)
            {
                mixed[i] *= _lastNormalizationFactor;
            }
            return mixed;
        }

        private static float SmoothNormalization(float previous, float target)
        {
            float maxStepUp = 0.05f; // gentle increase
            float maxStepDown = 0.15f; // allow moderate decrease
            float delta = target - previous;
            if (delta > maxStepUp) delta = maxStepUp; else if (delta < -maxStepDown) delta = -maxStepDown;
            return previous + delta;
        }

        private void ApplyEnhancedLimiting(float[] audioData)
        {
            const float threshold = 0.85f; const float ratio = 8f;
            for (int i = 0; i < audioData.Length; i++)
            {
                float abs = Math.Abs(audioData[i]);
                if (abs > threshold)
                {
                    float excess = abs - threshold; float comp = excess / ratio; float newLevel = threshold + comp;
                    audioData[i] = Math.Sign(audioData[i]) * Math.Min(newLevel, 0.9f); _currentStats.LimiterTriggerCount++;
                }
            }
        }

        private void ApplyDynamicGainControlSmooth(float[] audioData, bool isMultiStream)
        {
            float rmsSum = 0; for (int i = 0; i < audioData.Length; i++) rmsSum += audioData[i] * audioData[i];
            float rms = (float)Math.Sqrt(rmsSum / audioData.Length);
            float targetLevel = isMultiStream ? 0.4f : 0.5f;
            if (rms > 0.005f)
            {
                float gain = Math.Min(targetLevel / rms, 1.1f); gain = Math.Max(gain, 0.4f);
                float threshold = isMultiStream ? 0.05f : 0.1f;
                if (Math.Abs(gain - 1f) > threshold)
                {
                    float smooth = 1f + (gain - 1f) * 0.3f;
                    for (int i = 0; i < audioData.Length; i++) audioData[i] *= smooth;
                }
            }
        }

        private void UpdateStatistics(float[] audioData, int activeStreamCount)
        {
            float sumSquares = 0; float peak = 0;
            for (int i = 0; i < audioData.Length; i++) { float sample = Math.Abs(audioData[i]); sumSquares += audioData[i] * audioData[i]; if (sample > peak) peak = sample; }
            _currentStats.CurrentRms = (float)Math.Sqrt(sumSquares / audioData.Length);
            _currentStats.CurrentPeak = peak;
            _currentStats.CurrentGainDb = 20 * (float)Math.Log10(Math.Max(_currentStats.CurrentRms, 1e-10f));
            _currentStats.ActiveStreamCount = activeStreamCount;
            OnStatsUpdated?.Invoke(_currentStats);
        }

        public void StopAudioStream(AudioType audioType)
        {
            if (_audioStreams.TryGetValue(audioType, out var processor)) { processor.Stop(); _logger.LogDebug("Stopped audio stream for {AudioType}", audioType); _hasPendingData = true; }
        }

        public void ClearAllBuffers()
        {
            lock (_filterLock)
            {
                foreach (var processor in _audioStreams.Values) processor.ClearBuffer();
                _volumeStates.Clear(); SetState(AudioMixerState.Idle); _hasPendingData = false; _logger.LogDebug("Cleared all audio buffers");
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
                ActiveStreamCount = _audioStreams.Count(kvp => kvp.Value.HasAnyData() && !kvp.Value.IsComplete),
                DelayCompensation = new Dictionary<AudioType, float>()
            };
        }

        private void SetState(AudioMixerState newState)
        {
            if (_state != newState) { _state = newState; StateChanged?.Invoke(_state); _logger.LogDebug("FFmpeg audio mixer state changed to {State}", _state); }
        }

        private void CleanupFilterGraph()
        {
            if (_filterGraph != null)
            {
                var graph = _filterGraph; ffmpeg.avfilter_graph_free(&graph); _filterGraph = null;
            }
            _sourceFilters.Clear(); _amixFilterCtx = null; _sinkFilterCtx = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_filterLock)
            {
                SetState(AudioMixerState.Stopped);
                _processingTimer?.Change(Timeout.Infinite, Timeout.Infinite); _processingTimer?.Dispose();
                SpinWait.SpinUntil(() => _processingFlag == 0, 1000);
                foreach (var processor in _audioStreams.Values) processor.Dispose();
                _audioStreams.Clear(); _volumeStates.Clear();
                CleanupFilterGraph();
                _disposed = true; _initialized = false;
            }
            _logger.LogInformation("FFmpeg audio mixer disposed");
        }
    }
}