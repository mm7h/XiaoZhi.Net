using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        private int _frameSampleCount;

        // State management
        private bool _disposed = false;
        private AudioMixerState _state = AudioMixerState.Idle;
        private readonly AudioMixerStats _currentStats = new();

        // Enhanced volume control for priority-based mixing
        private Dictionary<AudioType, float>? _baseVolumeLevels;
        private Dictionary<AudioType, float>? _prioritySuppressionLevels;

        // Track last active types to avoid restarting transitions too often
        private HashSet<AudioType> _lastActiveTypes = [];
        // Track last highest priority to detect priority change even if active set stays same
        private int? _lastHighestPriority = null;

        private float _lastNormalizationFactor = 0.75f;

        // Only mark the very first mixed frame after switching to Mixing
        private volatile bool _firstFrameAfterStart = false;
        // Ensure we emit isLast only once per mixing session
        private volatile bool _lastFrameEmitted = false;

        private readonly Queue<OutputBufferFrame> _outputBuffer = new();
        private readonly object _bufferLock = new();
        private DateTime _lastScheduledOutputTime = DateTime.MinValue;
        private bool _bufferPreFilled = false;
        private bool _playbackStarted = false;

        public AudioMixer(ILogger<AudioMixer>? logger = null)
        {
            this._logger = logger ?? NullLogger<AudioMixer>.Instance;

            this._mixingTimer = new Timer(this.ProcessMixingCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public event Action<AudioMixerState>? OnStateChanged;
        public event Action<float[], bool, bool, string?>? OnMixedAudioDataAvailable;

        public event Action<AudioMixerStats>? OnMixingStatsUpdated;

        public bool IsInitialized { get; private set; } = false;
        public int OutputSampleRate { get; private set; }
        public int OutputChannels { get; private set; }
        public int FrameDuration { get; private set; }

        public bool Initialize(int outputSampleRate, int outputChannels, int frameDuration, AudioMixerConfig? config = null)
        {
            lock (this._syncLock)
            {
                try
                {
                    if (this.IsInitialized)
                    {
                        this._logger.LogWarning("AudioMixer is already initialized");
                        return true;
                    }

                    if (config is not null)
                    {
                        this._config = config;
                    }

                    this._baseVolumeLevels = new()
                    {
                        { AudioType.SystemNotification, this._config.SystemNotificationVolumeConfig.BaseVolume },
                        { AudioType.TTS, this._config.TTSVolumeConfig.BaseVolume },
                        { AudioType.Music, this._config.MusicVolumeConfig.BaseVolume },
                        { AudioType.Other, 0.5f }
                    };

                    this._prioritySuppressionLevels = new()
                    {
                        { AudioType.SystemNotification, this._config.SystemNotificationVolumeConfig.SuppressionVolume },
                        { AudioType.TTS, this._config.TTSVolumeConfig.SuppressionVolume },
                        { AudioType.Music, this._config.MusicVolumeConfig.SuppressionVolume },
                        { AudioType.Other, 0.05f }
                    };

                    this.OutputSampleRate = outputSampleRate;
                    this.OutputChannels = outputChannels;
                    this.FrameDuration = frameDuration;
                    this._frameSampleCount = outputSampleRate * frameDuration / 1000 * outputChannels;
                    // Use a higher tick than frame to drive smoother transitions
                    var timerInterval = Math.Max(frameDuration / 4, 5);
                    this._mixingTimer.Change(timerInterval, timerInterval);

                    this.IsInitialized = true;
                    this.SetState(AudioMixerState.Idle);

                    this._logger.LogInformation("AudioMixer initialized: {SampleRate}Hz, {Channels} channels, {FrameDuration}ms frames, timer interval: {TimerInterval}ms",
                        outputSampleRate, outputChannels, frameDuration, timerInterval);

                    return true;
                }
                catch (Exception ex)
                {
                    this._logger.LogError(ex, "Failed to initialize AudioMixer");
                    return false;
                }
            }
        }

        private void EmitMixedAudio(float[] mixedData, bool isFirst, bool isLast, string? sentenceId)
        {
            int targetBufferDepth = this._config.MaxOutputBufferFrames;
            if (targetBufferDepth <= 0)
            {
                targetBufferDepth = 1;
            }

            while (true)
            {
                if (this._disposed)
                {
                    return;
                }

                bool canEnqueue = false;
                DateTime nextIdealTime;
                lock (this._bufferLock)
                {
                    if (this._lastScheduledOutputTime == DateTime.MinValue)
                    {
                        int initialDelay = this._config.BufferPrefillFrames * this.FrameDuration;
                        this._lastScheduledOutputTime = DateTime.UtcNow.AddMilliseconds(initialDelay);
                        this._logger.LogInformation("Buffer pacing baseling set with prefill delay {delay}ms", initialDelay);
                    }

                    if (this._outputBuffer.Count < targetBufferDepth)
                    {
                        nextIdealTime = this._lastScheduledOutputTime.AddMilliseconds(this.FrameDuration);
                        var frame = new OutputBufferFrame([.. mixedData], isFirst, isLast, sentenceId);
                        this._outputBuffer.Enqueue(frame);
                        this._lastScheduledOutputTime = nextIdealTime;
                        if (!this._bufferPreFilled && this._outputBuffer.Count >= this._config.BufferPrefillFrames)
                        {
                            this._bufferPreFilled = true;
                            this._logger.LogInformation("Prefilled {count} frames. Starting pacing timer.", this._outputBuffer.Count);
                            this.StartPlaybackTimer();
                        }
                        canEnqueue = true;
                    }
                }
                if (canEnqueue)
                {
                    break;
                }

                Thread.Sleep(Math.Min(2, this.FrameDuration / 4));
            }
        }

        private void StartPlaybackTimer()
        {
            if (this._playbackStarted)
            {
                return;
            }

            this._playbackStarted = true;
            this._playbackTimer = new Timer(this.PlaybackTimerCallback, null, 0, this.FrameDuration);
        }

        private void PlaybackTimerCallback(object? state)
        {
            if (!this._bufferPreFilled || this._disposed)
            {
                return;
            }

            OutputBufferFrame? frameToPlay = null;
            lock (this._bufferLock)
            {
                if (this._outputBuffer.Count > 0)
                {
                    frameToPlay = this._outputBuffer.Dequeue();
                }
            }
            if (frameToPlay.HasValue)
            {
                var frame = frameToPlay.Value;
                this.OutputAudioData(frame.Data, frame.IsFirst, frame.IsLast, frame.SentenceId);
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
                this._logger?.LogError(ex, "Error in OnMixedAudioDataAvailable callback");
            }
        }

        public void AddAudioData(AudioType audioType, float[] audioData, string? sentenceId = null)
        {
            if (!this.IsInitialized || this._disposed || audioData == null)
            {
                return;
            }

            if (audioData.Length == 0 && string.IsNullOrEmpty(sentenceId))
            {
                return;
            }


            // Get or create input stream for this audio type
            var audioInput = this._audioInputs.GetOrAdd(audioType,
                _ => new AudioStreamProcessor(audioType, this.OutputSampleRate, this.OutputChannels, this.FrameDuration, this._config));

            this._volumeStates.GetOrAdd(audioType, _ => new VolumeTransitionControl());

            // Add data to the input stream with automatic frame boundary detection
            audioInput.AddData(audioData, sentenceId);

            // Reset last-frame flag as new data arrived in current session
            this._lastFrameEmitted = false;

            // Only when new stream first frame enters do we recompute
            if (audioInput.ProcessedFrameCount == 0 && audioInput.IsFirstFrame)
            {
                this.UpdateVolumeTargets();
            }

            this._hasPendingData = true;

            if (this._state == AudioMixerState.Idle)
            {
                this.SetState(AudioMixerState.Mixing);
            }

            this.TryProcessMixingImmediate();
        }

        private void UpdateVolumeTargets()
        {
            if (!this._config.EnableSmoothVolumeControl)
            {
                return;
            }

            // Active set determined by started-but-not-completed streams to avoid jitter
            var activeTypes = this._audioInputs
                .Where(kvp => !kvp.Value.IsComplete && (kvp.Value.HasAnyData() || kvp.Value.ProcessedFrameCount > 0 || kvp.Value.IsStopping))
                .Select(kvp => kvp.Key)
                .OrderBy(t => t)
                .ToList();

            if (activeTypes.Count == 0)
            {
                return;
            }

            var highestPriority = activeTypes.Max(t => (int)t);

            // Recompute when active set or highest priority changes
            bool activeUnchanged = this._lastActiveTypes.SetEquals(activeTypes);
            bool priorityUnchanged = this._lastHighestPriority.HasValue && this._lastHighestPriority.Value == highestPriority;
            if (activeUnchanged && priorityUnchanged)
            {
                return;
            }

            this._lastActiveTypes = [.. activeTypes];
            this._lastHighestPriority = highestPriority;

            foreach (var audioType in activeTypes)
            {
                var volumeState = this._volumeStates.GetOrAdd(audioType, _ => new VolumeTransitionControl());
                var targetVolume = this.CalculateTargetVolume(audioType, highestPriority, activeTypes);

                // Only start transition if target actually changes, to avoid repeated log spam
                if (MathF.Abs(volumeState.TargetVolume - targetVolume) > 1e-3f)
                {
                    volumeState.StartTransition(targetVolume, this._config.VolumeTransitionDurationMs, this._config.TransitionCurve);

                    this._logger.LogDebug("Volume transition started for {AudioType}: {Current:F3} -> {Target:F3}",
                        audioType, volumeState.CurrentVolume, targetVolume);
                }
            }

            // Remove states for streams no longer active to allow future clean restarts
            foreach (var stale in this._volumeStates.Keys.ToList())
            {
                if (!activeTypes.Contains(stale))
                {
                    this._volumeStates.TryRemove(stale, out _);
                }
            }
        }

        private float CalculateTargetVolume(AudioType audioType, int highestPriority, List<AudioType> activeTypes)
        {
            var baseVolume = this._baseVolumeLevels?.GetValueOrDefault(audioType, 0.5f) ?? 0.5f;
            var currentPriority = (int)audioType;

            if (currentPriority == highestPriority)
            {
                return baseVolume;
            }

            var suppressionVolume = this._prioritySuppressionLevels?.GetValueOrDefault(audioType, 0.05f) ?? 0.05f;
            var higherPriorityCount = activeTypes.Count(t => (int)t > currentPriority);

            if (higherPriorityCount > 0)
            {
                suppressionVolume *= (float)Math.Pow(0.5, higherPriorityCount - 1);
            }

            return suppressionVolume;
        }

        private void TryProcessMixingImmediate()
        {
            if (Interlocked.CompareExchange(ref this._processingFlag, 1, 0) == 0)
            {
                try
                {
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            this.ProcessMixing();
                        }
                        finally
                        {
                            Interlocked.Exchange(ref this._processingFlag, 0);
                        }
                    }, TaskCreationOptions.LongRunning);
                }
                catch
                {
                    Interlocked.Exchange(ref this._processingFlag, 0);
                }
            }
        }

        private void ProcessMixingCallback(object? state)
        {
            if (!this.IsInitialized || this._disposed)
            {
                return;
            }

            bool hasActiveTransitions = this._volumeStates.Values.Any(v => v.IsTransitioning);
            if (!this._hasPendingData && !hasActiveTransitions)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref this._processingFlag, 1, 0) == 0)
            {
                try
                {
                    this.ProcessMixing();
                }
                finally
                {
                    Interlocked.Exchange(ref this._processingFlag, 0);
                }
            }
        }

        private void ProcessMixing()
        {
            if (this._audioInputs.IsEmpty)
            {
                this._hasPendingData = false;
                return;
            }

            try
            {
                bool hasProcessedData = false;
                int processedFrameCount = 0;
                const int MaxFramesPerCycle = 3; // CPU guard

                while (processedFrameCount < MaxFramesPerCycle)
                {
                    lock (this._bufferLock)
                    {
                        if (this._outputBuffer.Count >= Math.Max(1, this._config.MaxOutputBufferFrames))
                        {
                            break;
                        }
                    }

                    var allInputs = this._audioInputs.Values.ToList();
                    var inputsWithData = allInputs.Where(input => input.HasAnyData() && !input.IsComplete).ToList();

                    if (inputsWithData.Count == 0)
                    {
                        // Transition-only period: advance transitions and emit silent frames
                        bool hadTransitions = this._volumeStates.Values.Any(v => v.IsTransitioning);
                        if (hadTransitions && allInputs.Count > 0)
                        {
                            foreach (var v in this._volumeStates.Values)
                            {
                                _ = v.UpdateAndGetCurrentVolume();
                            }

                            bool transitionsStill = this._volumeStates.Values.Any(v => v.IsTransitioning);
                            bool allComplete = this._audioInputs.Values.All(i => i.IsComplete && !i.HasAnyData());
                            bool shouldMarkLast = allComplete && !transitionsStill;

                            var silentFrame = new float[this._frameSampleCount];

                            bool isFirst = false;
                            if (this._firstFrameAfterStart)
                            {
                                isFirst = true;
                                this._firstFrameAfterStart = false;
                            }

                            bool markLastNow = shouldMarkLast && !this._lastFrameEmitted;
                            this.EmitMixedAudio(silentFrame, isFirst, markLastNow, null);
                            if (markLastNow)
                            {
                                this._lastFrameEmitted = true;
                            }

                            hasProcessedData = true;
                            processedFrameCount++;
                            continue;
                        }

                        bool allCompleteAndEmpty = allInputs.Count > 0 && this._audioInputs.Values.All(i => i.IsComplete && !i.HasAnyData());
                        if (allCompleteAndEmpty && !this._lastFrameEmitted)
                        {
                            // Only emit the final frame once
                            if (!this._lastFrameEmitted)
                            {
                                var silentFrame = new float[this._frameSampleCount];

                                bool isFirst = false;
                                if (this._firstFrameAfterStart)
                                {
                                    isFirst = true;
                                    this._firstFrameAfterStart = false;
                                }

                                this.EmitMixedAudio(silentFrame, isFirst, true, null);
                                this._lastFrameEmitted = true;
                                hasProcessedData = true;
                            }

                            break;
                        }

                        break;
                    }

                    // buffer strategy to allow partial on new streams
                    var activeInputs = this.GetActiveInputsWithBufferStrategy(inputsWithData);
                    if (activeInputs.Count == 0)
                    {
                        break;
                    }

                    var currentActiveTypes = activeInputs.Select(input => input.AudioType).OrderBy(t => t).ToList();

                    // If active set changed (some completed mid-loop), trigger update once.
                    if (!this._lastActiveTypes.SetEquals(currentActiveTypes))
                    {
                        this.UpdateVolumeTargets();
                    }

                    var (mixedAudio, sentenceId) = this.MixAudioStreamsWithSmoothVolume(activeInputs);
                    if (mixedAudio == null)
                    {
                        break;
                    }

                    if (mixedAudio.Length > 0)
                    {
                        this.ApplyEnhancedLimiting(mixedAudio);
                        this.ApplyDynamicGainControlSmooth(mixedAudio, activeInputs.Count > 1);
                        this.UpdateStatistics(mixedAudio, activeInputs.Count);
                    }

                    bool isFirstFrame = false;
                    if (this._firstFrameAfterStart)
                    {
                        isFirstFrame = true;
                        this._firstFrameAfterStart = false;
                    }

                    bool allCompleteNow = this._audioInputs.Values.All(i => i.IsComplete && !i.HasAnyData());
                    bool hasTransitions = this._volumeStates.Values.Any(v => v.IsTransitioning);
                    bool isLastCandidate = allCompleteNow && !hasTransitions;
                    bool markLast = isLastCandidate && !this._lastFrameEmitted;

                    this.EmitMixedAudio(mixedAudio, isFirstFrame, markLast, sentenceId);
                    if (markLast)
                    {
                        this._lastFrameEmitted = true;
                    }

                    foreach (var input in activeInputs)
                    {
                        input.MarkFrameProcessed();
                    }

                    hasProcessedData = true;
                    processedFrameCount++;
                }

                // Cleanup completed streams
                var completedStreams = this._audioInputs.Where(kvp => kvp.Value.IsComplete && !kvp.Value.HasAnyData()).ToList();
                bool hasCompletedStreams = completedStreams.Count > 0;

                foreach (var completedStream in completedStreams)
                {
                    var audioType = completedStream.Key;

                    if (this._audioInputs.TryRemove(audioType, out var stream))
                    {
                        stream.Dispose();
                        this._volumeStates.TryRemove(audioType, out _); // remove transition state
                    }
                }

                if (hasCompletedStreams && this._audioInputs.Count > 0)
                {
                    this._logger.LogDebug("Audio stream completed, recalculating volume targets for remaining streams");
                    this.UpdateVolumeTargets(); // recompute for remaining streams
                    this._hasPendingData = true;
                }

                // Update state
                if (this._audioInputs.IsEmpty)
                {
                    this.SetState(AudioMixerState.Idle);
                    this._hasPendingData = false;
                    this._lastActiveTypes.Clear();
                    this._lastHighestPriority = null;
                }
                else if (!hasProcessedData)
                {
                    var hasAnyData = this._audioInputs.Values.Any(input => input.HasAnyData());
                    var hasActiveTransitions = this._volumeStates.Values.Any(v => v.IsTransitioning);
                    if (!hasAnyData && !hasActiveTransitions)
                    {
                        this._hasPendingData = false;
                    }
                }
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error during audio mixing");
                this._hasPendingData = false;
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

                bool hasFullFrame = input.HasDataForFrame(this._frameSampleCount);
                bool isNewStream = input.ProcessedFrameCount < 3;

                if (hasFullFrame)
                {
                    activeInputs.Add(input);
                }
                else if (isNewStream && this._config.EnableSmoothVolumeControl)
                {
                    // Allow partial frame on new streams to reduce startup latency
                    int minRequiredSamples = (int)(this._frameSampleCount * this._config.NewStreamBufferTolerance);
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

        private (float[]? data, string? sentenceId) MixAudioStreamsWithSmoothVolume(List<AudioStreamProcessor> activeInputs)
        {
            if (activeInputs.Count == 0)
            {
                return (null, null);
            }

            var mixedAudio = new float[this._frameSampleCount];
            string? selectedSentenceId = null;
            bool hasAudioContent = false;

            // Track per-stream energy to decide normalization participants
            var streamEnergies = new List<(AudioStreamProcessor stream, float energy, bool warmup)>();

            foreach (var input in activeInputs)
            {
                var frameData = input.GetFrameDataWithPartialSupport(this._frameSampleCount, out int samplesRead, out string? sentenceId);
                if (frameData == null)
                {
                    continue;
                }

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
                const int FadeLength = 16;
                if (input.IsFirstFrame)
                {
                    int len = Math.Min(FadeLength, frameData.Length);
                    for (int i = 0; i < len; i++)
                    {
                        frameData[i] *= (float)i / len;
                    }
                }
                if (input.IsLastFrame)
                {
                    int len = Math.Min(FadeLength, frameData.Length);
                    int start = frameData.Length - len;
                    if (start < 0)
                    {
                        start = 0;
                    }

                    for (int i = start; i < frameData.Length; i++)
                    {
                        float gain = 1f - ((float)(i - start) / len);
                        frameData[i] *= gain;
                    }
                }

                var volumeState = this._volumeStates.GetOrAdd(input.AudioType, _ => new VolumeTransitionControl());
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
                this._lastNormalizationFactor = SmoothNormalization(this._lastNormalizationFactor, targetNorm);
                for (int i = 0; i < mixedAudio.Length; i++)
                {
                    mixedAudio[i] *= this._lastNormalizationFactor;
                }

                return (mixedAudio, selectedSentenceId);
            }

            // Decide which streams participate in normalization (exclude very low energy / warmup streams)
            const float EnergyThreshold = 0.003f; // small value
            var effective = streamEnergies.Where(e => !e.warmup && e.energy >= EnergyThreshold).ToList();
            int effectiveCount = effective.Count;
            if (effectiveCount == 0)
            {
                effectiveCount = 1; // avoid division instability
            }

            float targetFactor = (float)(0.75 / Math.Sqrt(effectiveCount));
            // Smooth normalization changes to avoid sudden dip when a new low-energy stream enters
            this._lastNormalizationFactor = SmoothNormalization(this._lastNormalizationFactor, targetFactor);

            for (int i = 0; i < mixedAudio.Length; i++)
            {
                mixedAudio[i] *= this._lastNormalizationFactor;
            }

            return (mixedAudio, selectedSentenceId);
        }

        private static float SmoothNormalization(float previous, float target)
        {
            // Limit change per frame to avoid abrupt gain shifts (attack slower than release)
            float maxStepUp = 0.05f;   // allow small increases
            float maxStepDown = 0.15f; // allow moderate decreases
            float delta = target - previous;
            if (delta > maxStepUp)
            {
                delta = maxStepUp;
            }
            else if (delta < -maxStepDown)
            {
                delta = -maxStepDown;
            }

            return previous + delta;
        }

        private void ApplyEnhancedLimiting(float[] audioData)
        {
            const float Threshold = 0.85f;
            const float Ratio = 8.0f;

            for (int i = 0; i < audioData.Length; i++)
            {
                float absLevel = Math.Abs(audioData[i]);
                if (absLevel > Threshold)
                {
                    float excess = absLevel - Threshold;
                    float compressedExcess = excess / Ratio;
                    float newLevel = Threshold + compressedExcess;

                    audioData[i] = Math.Sign(audioData[i]) * Math.Min(newLevel, 0.9f);
                    this._currentStats.LimiterTriggerCount++;
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
                    float smoothedGain = 1.0f + ((gain - 1.0f) * 0.3f);
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

            this._currentStats.CurrentRms = (float)Math.Sqrt(sumSquares / audioData.Length);
            this._currentStats.CurrentPeak = peak;
            this._currentStats.CurrentGainDb = 20 * (float)Math.Log10(Math.Max(this._currentStats.CurrentRms, 1e-10f));
            this._currentStats.ActiveStreamCount = activeStreamCount;

            OnMixingStatsUpdated?.Invoke(this._currentStats);
        }

        public void StopAudioStream(AudioType audioType)
        {
            if (this._audioInputs.TryGetValue(audioType, out var audioInput))
            {
                audioInput.Stop();
                this._logger.LogDebug("Stopped audio stream for {AudioType}", audioType);
                this._hasPendingData = true;
            }
        }

        public void ClearAllBuffers()
        {
            lock (this._syncLock)
            {
                foreach (var input in this._audioInputs.Values)
                {
                    input.ClearBuffer();
                }
                this._volumeStates.Clear();
                this._lastActiveTypes.Clear();
                this._lastHighestPriority = null;
                lock (this._bufferLock)
                {
                    this._outputBuffer.Clear();
                    this._bufferPreFilled = false;
                }
                this.SetState(AudioMixerState.Idle);
                this._hasPendingData = false;

                this._logger.LogDebug("Cleared all audio buffers");
            }
        }

        public AudioMixerStats GetCurrentStats()
        {
            return new AudioMixerStats
            {
                CurrentRms = this._currentStats.CurrentRms,
                CurrentPeak = this._currentStats.CurrentPeak,
                CurrentGainDb = this._currentStats.CurrentGainDb,
                LimiterTriggerCount = this._currentStats.LimiterTriggerCount,
                ActiveStreamCount = this._audioInputs.Count,
                DelayCompensation = []
            };
        }

        private void SetState(AudioMixerState newState)
        {
            if (this._state != newState)
            {
                this._state = newState;
                if (newState == AudioMixerState.Mixing)
                {
                    this._firstFrameAfterStart = true;
                    this._lastFrameEmitted = false; // reset last-frame flag on start
                }
                else
                {
                    this._firstFrameAfterStart = false;
                }
                OnStateChanged?.Invoke(this._state);
                this._logger.LogDebug("AudioMixer state changed to {State}", this._state);
            }
        }

        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            lock (this._syncLock)
            {
                lock (this._bufferLock)
                {
                    while (this._outputBuffer.Count > 0)
                    {
                        var frame = this._outputBuffer.Dequeue();
                        this.OutputAudioData(frame.Data, frame.IsFirst, frame.IsLast, frame.SentenceId);
                    }
                }
                this.SetState(AudioMixerState.Stopped);
                this._mixingTimer?.Change(Timeout.Infinite, Timeout.Infinite); this._mixingTimer?.Dispose();
                this._playbackTimer?.Change(Timeout.Infinite, Timeout.Infinite); this._playbackTimer?.Dispose();
                SpinWait.SpinUntil(() => this._processingFlag == 0, 1000);
                foreach (var input in this._audioInputs.Values)
                {
                    input.Dispose();
                }

                this._audioInputs.Clear(); this._volumeStates.Clear(); this._lastActiveTypes.Clear(); this._lastHighestPriority = null;
                this._disposed = true; this.IsInitialized = false; this._logger.LogInformation("AudioMixer disposed");
            }
        }
    }
}
