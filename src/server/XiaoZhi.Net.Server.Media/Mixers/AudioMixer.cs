using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Common.Models;

namespace XiaoZhi.Net.Server.Media.Mixers
{
    /// <summary>
    /// Audio mixer with advanced volume control and smooth transitions
    /// </summary>
    internal class AudioMixer : IAudioMixer
    {
        private readonly ILogger<AudioMixer> _logger;
        private readonly object _syncLock = new();
        private readonly ConcurrentDictionary<AudioType, AudioStreamProcessor> _audioInputs = new();
        private readonly ConcurrentDictionary<AudioType, VolumeTransitionControl> _volumeStates = new();
        private readonly Timer _mixingTimer;
        private Timer? _playbackTimer;
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
        private Dictionary<AudioType, float>? _prioritySuppressionLevels;

        // Track last active types to avoid restarting transitions too often
        private HashSet<AudioType> _lastActiveTypes = new();
        // Track last highest priority to detect priority change even if active set stays same
        private int? _lastHighestPriority = null;

        private float _lastNormalizationFactor = 0.75f;

        // Only mark the very first mixed frame after switching to Mixing
        private volatile bool _firstFrameAfterStart = false;
        // Ensure we emit isLast only once per mixing session
        private volatile bool _lastFrameEmitted = false;

        private readonly Queue<OutputBufferFrame> _outputBuffer = new Queue<OutputBufferFrame>();
        private readonly object _bufferLock = new();
        private DateTime _lastScheduledOutputTime = DateTime.MinValue;
        private bool _bufferPreFilled = false;
        private bool _playbackStarted = false;

        public AudioMixer(ILogger<AudioMixer>? logger = null)
        {
            _logger = logger ?? NullLogger<AudioMixer>.Instance;

            _mixingTimer = new Timer(ProcessMixingCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public event Action<AudioMixerState>? OnStateChanged;
        public event Action<float[], bool, bool, string?>? OnMixedAudioDataAvailable;

        public event Action<AudioMixerStats>? OnMixingStatsUpdated;

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
                        _logger.LogWarning("AudioMixer is already initialized");
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
                    // Use a higher tick than frame to drive smoother transitions
                    var timerInterval = Math.Max(frameDuration / 4, 5);
                    _mixingTimer.Change(timerInterval, timerInterval);

                    _initialized = true;
                    SetState(AudioMixerState.Idle);

                    _logger.LogInformation("AudioMixer initialized: {SampleRate}Hz, {Channels} channels, {FrameDuration}ms frames, timer interval: {TimerInterval}ms",
                        outputSampleRate, outputChannels, frameDuration, timerInterval);

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize AudioMixer");
                    return false;
                }
            }
        }

        private void EmitMixedAudio(float[] mixedData, bool isFirst, bool isLast, string? sentenceId)
        {
            int targetBufferDepth = _config.MaxOutputBufferFrames;
            if (targetBufferDepth <= 0) targetBufferDepth = 1;


            while (true)
            {
                if (_disposed) return;
                bool canEnqueue = false;
                DateTime nextIdealTime;
                lock (_bufferLock)
                {
                    if (_lastScheduledOutputTime == DateTime.MinValue)
                    {
                        int initialDelay = _config.BufferPrefillFrames * _frameDuration;
                        _lastScheduledOutputTime = DateTime.UtcNow.AddMilliseconds(initialDelay);
                        _logger.LogInformation("Buffer pacing baseling set with prefill delay {delay}ms", initialDelay);
                    }

                    if (_outputBuffer.Count < targetBufferDepth)
                    {
                        nextIdealTime = _lastScheduledOutputTime.AddMilliseconds(_frameDuration);
                        var frame = new OutputBufferFrame(mixedData.ToArray(), isFirst, isLast, sentenceId);
                        _outputBuffer.Enqueue(frame);
                        _lastScheduledOutputTime = nextIdealTime;
                        if (!_bufferPreFilled && _outputBuffer.Count >= _config.BufferPrefillFrames)
                        {
                            _bufferPreFilled = true;
                            _logger.LogInformation("Prefilled {count} frames. Starting pacing timer.", _outputBuffer.Count);
                            StartPlaybackTimer();
                        }
                        canEnqueue = true;
                    }
                }
                if (canEnqueue) break;
                Thread.Sleep(Math.Min(2, _frameDuration / 4));
            }
        }

        private void StartPlaybackTimer()
        {
            if (_playbackStarted) return;
            _playbackStarted = true;
            _playbackTimer = new Timer(PlaybackTimerCallback, null, 0, _frameDuration);
        }

        private void PlaybackTimerCallback(object? state)
        {
            if (!_bufferPreFilled || _disposed) return;
            OutputBufferFrame? frameToPlay = null;
            lock (_bufferLock)
            {
                if (_outputBuffer.Count > 0)
                {
                    frameToPlay = _outputBuffer.Dequeue();
                }
            }
            if (frameToPlay.HasValue)
            {
                var frame = frameToPlay.Value;
                OutputAudioData(frame.Data, frame.IsFirst, frame.IsLast, frame.SentenceId);
            }
        }

        private void OutputAudioData(float[] data, bool isFirst, bool isLast, string? sentenceId)
        {
            try
            {
                OnMixedAudioDataAvailable?.Invoke(data, isFirst, isLast, sentenceId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in OnMixedAudioDataAvailable callback");
            }
        }

        public void AddAudioData(AudioType audioType, float[] audioData, string? sentenceId = null)
        {
            if (!_initialized || _disposed || audioData == null)
            {
                return;
            }

            if (audioData.Length == 0 && string.IsNullOrEmpty(sentenceId))
            {
                return;
            }


            // Get or create input stream for this audio type
            var audioInput = _audioInputs.GetOrAdd(audioType,
                _ => new AudioStreamProcessor(audioType, _outputSampleRate, _outputChannels, _frameDuration, _config));

            _volumeStates.GetOrAdd(audioType, _ => new VolumeTransitionControl());

            // Add data to the input stream with automatic frame boundary detection
            audioInput.AddData(audioData, sentenceId);

            // Reset last-frame flag as new data arrived in current session
            _lastFrameEmitted = false;

            // Only when new stream first frame enters do we recompute
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

            // Active set determined by started-but-not-completed streams to avoid jitter
            var activeTypes = _audioInputs
                .Where(kvp => !kvp.Value.IsComplete && (kvp.Value.HasAnyData() || kvp.Value.ProcessedFrameCount > 0 || kvp.Value.IsStopping))
                .Select(kvp => kvp.Key)
                .OrderBy(t => t)
                .ToList();

            if (activeTypes.Count == 0)
                return;

            var highestPriority = activeTypes.Max(t => (int)t);

            // Recompute when active set or highest priority changes
            bool activeUnchanged = _lastActiveTypes.SetEquals(activeTypes);
            bool priorityUnchanged = _lastHighestPriority.HasValue && _lastHighestPriority.Value == highestPriority;
            if (activeUnchanged && priorityUnchanged)
            {
                return;
            }

            _lastActiveTypes = new HashSet<AudioType>(activeTypes);
            _lastHighestPriority = highestPriority;

            foreach (var audioType in activeTypes)
            {
                var volumeState = _volumeStates.GetOrAdd(audioType, _ => new VolumeTransitionControl());
                var targetVolume = CalculateTargetVolume(audioType, highestPriority, activeTypes);

                // Only start transition if target actually changes, to avoid repeated log spam
                if (MathF.Abs(volumeState.TargetVolume - targetVolume) > 1e-3f)
                {
                    volumeState.StartTransition(targetVolume, _config.VolumeTransitionDurationMs, _config.TransitionCurve);

                    _logger.LogDebug("Volume transition started for {AudioType}: {Current:F3} -> {Target:F3}",
                        audioType, volumeState.CurrentVolume, targetVolume);
                }
            }

            // Remove states for streams no longer active to allow future clean restarts
            foreach (var stale in _volumeStates.Keys.ToList())
            {
                if (!activeTypes.Contains(stale))
                {
                    _volumeStates.TryRemove(stale, out _);
                }
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

            bool hasActiveTransitions = _volumeStates.Values.Any(v => v.IsTransitioning);
            if (!_hasPendingData && !hasActiveTransitions) return;

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
                const int maxFramesPerCycle = 3; // CPU guard

                while (processedFrameCount < maxFramesPerCycle)
                {
                    lock (_bufferLock)
                    {
                        if (_outputBuffer.Count >= Math.Max(1, _config.MaxOutputBufferFrames))
                        {
                            break;
                        }
                    }

                    var allInputs = _audioInputs.Values.ToList();
                    var inputsWithData = allInputs.Where(input => input.HasAnyData() && !input.IsComplete).ToList();

                    if (inputsWithData.Count == 0)
                    {
                        // Transition-only period: advance transitions and emit silent frames
                        bool hadTransitions = _volumeStates.Values.Any(v => v.IsTransitioning);
                        if (hadTransitions && allInputs.Count > 0)
                        {
                            foreach (var v in _volumeStates.Values)
                            {
                                _ = v.UpdateAndGetCurrentVolume();
                            }

                            bool transitionsStill = _volumeStates.Values.Any(v => v.IsTransitioning);
                            bool allComplete = _audioInputs.Values.All(i => i.IsComplete && !i.HasAnyData());
                            bool shouldMarkLast = allComplete && !transitionsStill;

                            var silentFrame = new float[_frameSampleCount];

                            bool isFirst = false;
                            if (_firstFrameAfterStart)
                            {
                                isFirst = true;
                                _firstFrameAfterStart = false;
                            }

                            bool markLastNow = shouldMarkLast && !_lastFrameEmitted;
                            EmitMixedAudio(silentFrame, isFirst, markLastNow, null);
                            if (markLastNow)
                            {
                                _lastFrameEmitted = true;
                            }

                            hasProcessedData = true;
                            processedFrameCount++;
                            continue;
                        }

                        bool allCompleteAndEmpty = allInputs.Count > 0 && _audioInputs.Values.All(i => i.IsComplete && !i.HasAnyData());
                        if (allCompleteAndEmpty && !_lastFrameEmitted)
                        {
                            // Only emit the final frame once
                            if (!_lastFrameEmitted)
                            {
                                var silentFrame = new float[_frameSampleCount];

                                bool isFirst = false;
                                if (_firstFrameAfterStart)
                                {
                                    isFirst = true;
                                    _firstFrameAfterStart = false;
                                }

                                EmitMixedAudio(silentFrame, isFirst, true, null);
                                _lastFrameEmitted = true;
                                hasProcessedData = true;
                            }

                            break;
                        }

                        break;
                    }

                    // buffer strategy to allow partial on new streams
                    var activeInputs = GetActiveInputsWithBufferStrategy(inputsWithData);
                    if (activeInputs.Count == 0)
                    {
                        break;
                    }

                    var currentActiveTypes = activeInputs.Select(input => input.AudioType).OrderBy(t => t).ToList();

                    // If active set changed (some completed mid-loop), trigger update once.
                    if (!_lastActiveTypes.SetEquals(currentActiveTypes))
                    {
                        UpdateVolumeTargets();
                    }

                    var (mixedAudio, sentenceId) = MixAudioStreamsWithSmoothVolume(activeInputs, currentActiveTypes);
                    if (mixedAudio == null)
                        break;
                    
                    if (mixedAudio.Length > 0)
                    {
                        ApplyEnhancedLimiting(mixedAudio);
                        ApplyDynamicGainControlSmooth(mixedAudio, activeInputs.Count > 1);
                        UpdateStatistics(mixedAudio, activeInputs.Count);
                    }

                    bool isFirstFrame = false;
                    if (_firstFrameAfterStart)
                    {
                        isFirstFrame = true;
                        _firstFrameAfterStart = false;
                    }

                    bool allCompleteNow = _audioInputs.Values.All(i => i.IsComplete && !i.HasAnyData());
                    bool hasTransitions = _volumeStates.Values.Any(v => v.IsTransitioning);
                    bool isLastCandidate = allCompleteNow && !hasTransitions;
                    bool markLast = isLastCandidate && !_lastFrameEmitted;

                    EmitMixedAudio(mixedAudio, isFirstFrame, markLast, sentenceId);
                    if (markLast)
                    {
                        _lastFrameEmitted = true;
                    }

                    foreach (var input in activeInputs)
                    {
                        input.MarkFrameProcessed();
                    }

                    hasProcessedData = true;
                    processedFrameCount++;
                }

                // Cleanup completed streams
                var completedStreams = _audioInputs.Where(kvp => kvp.Value.IsComplete && !kvp.Value.HasAnyData()).ToList();
                bool hasCompletedStreams = completedStreams.Count > 0;

                foreach (var completedStream in completedStreams)
                {
                    var audioType = completedStream.Key;

                    if (_audioInputs.TryRemove(audioType, out var stream))
                    {
                        stream.Dispose();
                        _volumeStates.TryRemove(audioType, out _); // remove transition state
                    }
                }

                if (hasCompletedStreams && _audioInputs.Count > 0)
                {
                    _logger.LogDebug("Audio stream completed, recalculating volume targets for remaining streams");
                    UpdateVolumeTargets(); // recompute for remaining streams
                    _hasPendingData = true;
                }

                // Update state
                if (_audioInputs.IsEmpty)
                {
                    SetState(AudioMixerState.Idle);
                    _hasPendingData = false;
                    _lastActiveTypes.Clear();
                    _lastHighestPriority = null;
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
                // Handle meta-only frames (no audio data but has metadata)
                if (input.AvailableDataCount == 0)
                {
                    activeInputs.Add(input);
                    continue;
                }

                bool hasFullFrame = input.HasDataForFrame(_frameSampleCount);
                bool isNewStream = input.ProcessedFrameCount < 3;

                if (hasFullFrame)
                {
                    activeInputs.Add(input);
                }
                else if (isNewStream && _config.EnableSmoothVolumeControl)
                {
                    // Allow partial frame on new streams to reduce startup latency
                    int minRequiredSamples = (int)(_frameSampleCount * _config.NewStreamBufferTolerance);
                    if (input.AvailableDataCount >= minRequiredSamples)
                    {
                        activeInputs.Add(input);
                    }
                }
                else if (input.IsStopping && input.HasAnyData())
                {
                    // Include stopping streams that have any data (audio or meta-only frames)
                    activeInputs.Add(input);
                }
            }

            return activeInputs;
        }

        private (float[]? data, string? sentenceId) MixAudioStreamsWithSmoothVolume(List<AudioStreamProcessor> activeInputs, List<AudioType> currentActiveTypes)
        {
            if (activeInputs.Count == 0)
            {
                return (null, null);
            }

            var mixedAudio = new float[_frameSampleCount];
            string? selectedSentenceId = null;
            bool hasAudioContent = false;

            // Track per-stream energy to decide normalization participants
            var streamEnergies = new List<(AudioStreamProcessor stream, float energy, bool warmup)>();

            foreach (var input in activeInputs)
            {
                int samplesRead;
                var frameData = input.GetFrameDataWithPartialSupport(_frameSampleCount, out samplesRead, out string? sentenceId);
                if (frameData == null)
                    continue;

                // Prioritize TTS sentence ID
                if (input.AudioType == AudioType.TTS && !string.IsNullOrEmpty(sentenceId))
                {
                    selectedSentenceId = sentenceId;
                }
                else if (selectedSentenceId == null && !string.IsNullOrEmpty(sentenceId))
                {
                    selectedSentenceId = sentenceId;
                }

                if (frameData.Length == 0)
                {
                    continue;
                }

                hasAudioContent = true;

                // short fade in/out to reduce clicks
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
                    int start = frameData.Length - len;
                    if (start < 0) start = 0;
                    for (int i = start; i < frameData.Length; i++)
                    {
                        float gain = 1f - (float)(i - start) / len;
                        frameData[i] *= gain;
                    }
                }

                var volumeState = _volumeStates.GetOrAdd(input.AudioType, _ => new VolumeTransitionControl());
                var currentVolume = volumeState.UpdateAndGetCurrentVolume();

                float absSum = 0f;
                int lenAll = Math.Min(mixedAudio.Length, frameData.Length);
                for (int i = 0; i < lenAll; i++)
                {
                    float sample = frameData[i] * currentVolume;
                    mixedAudio[i] += sample;
                    absSum += Math.Abs(sample);
                }
                float avgAbs = absSum / Math.Max(1, lenAll);
                bool warmup = input.ProcessedFrameCount == 0; // first frame after join
                streamEnergies.Add((input, avgAbs, warmup));
            }

            if (!hasAudioContent)
            {
                return (Array.Empty<float>(), selectedSentenceId);
            }

            if (streamEnergies.Count <= 1)
            {
                // Single stream: smooth normalization to target ~0.75
                float targetNorm = 0.75f;
                _lastNormalizationFactor = SmoothNormalization(_lastNormalizationFactor, targetNorm);
                for (int i = 0; i < mixedAudio.Length; i++) mixedAudio[i] *= _lastNormalizationFactor;
                return (mixedAudio, selectedSentenceId);
            }

            // Decide which streams participate in normalization (exclude very low energy / warmup streams)
            const float energyThreshold = 0.003f; // small value
            var effective = streamEnergies.Where(e => !e.warmup && e.energy >= energyThreshold).ToList();
            int effectiveCount = effective.Count;
            if (effectiveCount == 0) effectiveCount = 1; // avoid division instability

            float targetFactor = (float)(0.75 / Math.Sqrt(effectiveCount));
            // Smooth normalization changes to avoid sudden dip when a new low-energy stream enters
            _lastNormalizationFactor = SmoothNormalization(_lastNormalizationFactor, targetFactor);

            for (int i = 0; i < mixedAudio.Length; i++) mixedAudio[i] *= _lastNormalizationFactor;
            return (mixedAudio, selectedSentenceId);
        }

        private static float SmoothNormalization(float previous, float target)
        {
            // Limit change per frame to avoid abrupt gain shifts (attack slower than release)
            float maxStepUp = 0.05f;   // allow small increases
            float maxStepDown = 0.15f; // allow moderate decreases
            float delta = target - previous;
            if (delta > maxStepUp) delta = maxStepUp;
            else if (delta < -maxStepDown) delta = -maxStepDown;
            return previous + delta;
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

            OnMixingStatsUpdated?.Invoke(_currentStats);
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
                _lastActiveTypes.Clear();
                _lastHighestPriority = null;
                lock (_bufferLock)
                {
                    _outputBuffer.Clear();
                    _bufferPreFilled = false;
                }
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
                if (newState == AudioMixerState.Mixing)
                {
                    _firstFrameAfterStart = true;
                    _lastFrameEmitted = false; // reset last-frame flag on start
                }
                else
                {
                    _firstFrameAfterStart = false;
                }
                OnStateChanged?.Invoke(_state);
                _logger.LogDebug("AudioMixer state changed to {State}", _state);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_syncLock)
            {
                lock (_bufferLock)
                {
                    while (_outputBuffer.Count > 0)
                    {
                        var frame = _outputBuffer.Dequeue();
                        OutputAudioData(frame.Data, frame.IsFirst, frame.IsLast, frame.SentenceId);
                    }
                }
                SetState(AudioMixerState.Stopped);
                _mixingTimer?.Change(Timeout.Infinite, Timeout.Infinite); _mixingTimer?.Dispose();
                _playbackTimer?.Change(Timeout.Infinite, Timeout.Infinite); _playbackTimer?.Dispose();
                SpinWait.SpinUntil(() => _processingFlag == 0, 1000);
                foreach (var input in _audioInputs.Values) input.Dispose();
                _audioInputs.Clear(); _volumeStates.Clear(); _lastActiveTypes.Clear(); _lastHighestPriority = null;
                _disposed = true; _initialized = false; _logger.LogInformation("AudioMixer disposed");
            }
        }
    }
}