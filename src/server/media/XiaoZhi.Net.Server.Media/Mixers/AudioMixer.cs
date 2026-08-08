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
    /// 具有高级音量控制和平滑过渡效果的音频混音器。
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

        // 音频格式设置。
        private int _frameSampleCount;

        // 状态管理。
        private bool _disposed = false;
        private AudioMixerState _state = AudioMixerState.Idle;
        private readonly AudioMixerStats _currentStats = new();

        // 用于基于优先级混音的增强音量控制。
        private Dictionary<AudioType, float>? _baseVolumeLevels;
        private Dictionary<AudioType, float>? _prioritySuppressionLevels;

        // 跟踪上次活动的类型，避免过于频繁地重新开始过渡。
        private HashSet<AudioType> _lastActiveTypes = [];
        // 跟踪上次最高优先级，即使活动集合不变也可检测优先级变化。
        private int? _lastHighestPriority = null;

        private float _lastNormalizationFactor = 0.75f;

        // 仅标记切换到 Mixing 后的第一个混音帧。
        private volatile bool _firstFrameAfterStart = false;
        // 确保每个混音会话只输出一次 isLast。
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
                    // 使用比帧频更高的节拍驱动更平滑的过渡。
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
                        // mixedData 是当前混音周期独占的新数组，入队后由输出队列接管。
                        var frame = new OutputBufferFrame(mixedData, isFirst, isLast, sentenceId);
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


            // 获取或创建此音频类型的输入流。
            var audioInput = this._audioInputs.GetOrAdd(audioType,
                _ => new AudioStreamProcessor(audioType, this.OutputSampleRate, this.OutputChannels, this.FrameDuration, this._config));

            this._volumeStates.GetOrAdd(audioType, _ => new VolumeTransitionControl());

            // 将数据加入输入流，并自动检测帧边界。
            audioInput.AddData(audioData, sentenceId);

            // 当前会话收到新数据时重置末帧标志。
            this._lastFrameEmitted = false;

            // 仅当新流的首帧进入时重新计算。
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

            // 根据已开始但未完成的流确定活动集合，以避免抖动。
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

            // 当活动集合或最高优先级变化时重新计算。
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

                // 仅当目标确实变化时开始过渡，以避免重复日志。
                if (MathF.Abs(volumeState.TargetVolume - targetVolume) > 1e-3f)
                {
                    volumeState.StartTransition(targetVolume, this._config.VolumeTransitionDurationMs, this._config.TransitionCurve);

                    this._logger.LogDebug("Volume transition started for {AudioType}: {Current:F3} -> {Target:F3}",
                        audioType, volumeState.CurrentVolume, targetVolume);
                }
            }

            // 移除不再活动的流的状态，以便将来干净地重新开始。
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
                        // 仅过渡阶段：推进过渡并输出静音帧。
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
                            // 仅输出一次最终帧。
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

                    // 缓冲策略允许新流输出部分帧。
                    var activeInputs = this.GetActiveInputsWithBufferStrategy(inputsWithData);
                    if (activeInputs.Count == 0)
                    {
                        break;
                    }

                    var currentActiveTypes = activeInputs.Select(input => input.AudioType).OrderBy(t => t).ToList();

                    // 如果活动集合发生变化（部分流在循环中完成），则触发一次更新。
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

                // 清理已完成的流。
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

                // 更新状态。
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
                // 处理仅包含元数据、不含音频数据的帧。
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
                    // 允许新流输出部分帧，以降低启动延迟。
                    int minRequiredSamples = (int)(this._frameSampleCount * this._config.NewStreamBufferTolerance);
                    if (input.AvailableDataCount >= minRequiredSamples)
                    {
                        activeInputs.Add(input);
                    }
                }
                else if (input.IsStopping && input.HasAnyData())
                {
                    // 包含具有任意数据（音频或仅元数据帧）的正在停止的流。
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

            // 跟踪每条流的能量，以确定参与归一化的流。
            var streamEnergies = new List<(AudioStreamProcessor stream, float energy, bool warmup)>();

            foreach (var input in activeInputs)
            {
                var frameData = input.GetFrameDataWithPartialSupport(this._frameSampleCount, out int samplesRead, out string? sentenceId);
                if (frameData == null)
                {
                    continue;
                }

                // 优先使用 TTS 句子 ID。
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

                // 短暂淡入淡出以减少爆音。
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
                // 单流：平滑归一化到约 0.75 的目标值。
                float targetNorm = 0.75f;
                this._lastNormalizationFactor = SmoothNormalization(this._lastNormalizationFactor, targetNorm);
                for (int i = 0; i < mixedAudio.Length; i++)
                {
                    mixedAudio[i] *= this._lastNormalizationFactor;
                }

                return (mixedAudio, selectedSentenceId);
            }

            // 确定哪些流参与归一化（排除能量极低或预热中的流）。
            const float EnergyThreshold = 0.003f; // small value
            var effective = streamEnergies.Where(e => !e.warmup && e.energy >= EnergyThreshold).ToList();
            int effectiveCount = effective.Count;
            if (effectiveCount == 0)
            {
                effectiveCount = 1; // avoid division instability
            }

            float targetFactor = (float)(0.75 / Math.Sqrt(effectiveCount));
            // 平滑归一化变化，避免新的低能量流进入时音量突然降低。
            this._lastNormalizationFactor = SmoothNormalization(this._lastNormalizationFactor, targetFactor);

            for (int i = 0; i < mixedAudio.Length; i++)
            {
                mixedAudio[i] *= this._lastNormalizationFactor;
            }

            return (mixedAudio, selectedSentenceId);
        }

        private static float SmoothNormalization(float previous, float target)
        {
            // 限制每帧变化以避免增益突变（起音比释放更慢）。
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
