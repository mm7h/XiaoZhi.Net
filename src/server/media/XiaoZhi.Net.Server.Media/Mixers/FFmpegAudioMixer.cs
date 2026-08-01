using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Utilities.Extensions;

namespace XiaoZhi.Net.Server.Media.Mixers
{
    internal unsafe class FFmpegAudioMixer : IAudioMixer
    {
        #region Private fields

        private readonly ILogger<FFmpegAudioMixer> _logger;

        // Audio stream management
        private readonly Dictionary<AudioType, AudioStreamProcessor> _audioStreams;
        // private readonly Dictionary<AudioType, IntPtr> _audioFifos; // Removed in favor of AudioStreamProcessor buffer
        private readonly Dictionary<AudioType, bool> _sourceClosedStates; // Track FFmpeg source filter closed state
        private readonly Dictionary<AudioType, VolumeTransitionControl> _volumeStates;
        private readonly Dictionary<AudioType, float> _volumeLevels;
        private readonly Dictionary<AudioType, int> _priorities;
        private readonly object _streamLock = new();

        // FFmpeg filter graph related
        private AVFilterGraph* _filterGraph;
        private AVFilterContext* _sinkFilterCtx;
        private readonly Dictionary<AudioType, IntPtr> _sourceFilterCtxs; // AVFilterContext*

        // Audio format parameters
        private int _outputSampleRate;
        private int _outputChannels;
        private int _frameDuration;
        private int _frameSampleCount;
        private AVSampleFormat _sampleFormat;
        private ulong _channelLayout;

        // Configuration and state management
        private AudioMixerConfig _config = new();
        private bool _initialized;
        private bool _disposed;
        private volatile bool _isMixing = false;
        private volatile bool _shouldStop = false;
        private readonly object _filterLock = new();
        private AudioMixerState _currentState = AudioMixerState.Idle;

        // Processing thread and synchronization
        private Thread? _mixingThread;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ManualResetEventSlim _dataAvailableEvent = new(false);

        // Statistics
        private AudioMixerStats _currentStats = new();
        private long _pts = 0;
        private int _totalStreamCount = 0;
        private int _completedStreamCount = 0;

        // Output event flags
        private bool _hasEmittedFirst = false;
        private bool _hasEmittedLast = false;
        private bool _draining = false;
        private volatile bool _filterGraphDirty = false;

        // Output buffering mechanism - new
        private readonly Queue<OutputBufferFrame> _outputBuffer = new Queue<OutputBufferFrame>();
        private readonly Queue<string?> _pendingSentenceIds = new Queue<string?>();
        private readonly object _bufferLock = new();
        private Timer? _playbackTimer;
        private DateTime _lastScheduledOutputTime = DateTime.MinValue;
        private bool _bufferPreFilled = false;
        private bool _playbackStarted = false;
        private volatile bool _firstFrameAfterStart = false;
        private volatile bool _lastFrameEmitted = false;

        // When all inputs are gone, we enter a draining phase: keep draining the filter graph and
        // only emit the final last-frame once the output pacing buffer becomes empty.
        private volatile bool _pendingFinalLastFrame = false;

        private float _lastNormalizationFactor = 0.75f;

        // Output buffer frame structure
        private struct OutputBufferFrame
        {
            public float[] Data;
            public bool IsFirst;
            public bool IsLast;
            public string? SentenceId;

            public OutputBufferFrame(float[] data, bool isFirst, bool isLast, string? sentenceId)
            {
                Data = data;
                IsFirst = isFirst;
                IsLast = isLast;
                SentenceId = sentenceId;
            }
        }


        #endregion

        #region Events and properties

        public event Action<AudioMixerState>? OnStateChanged;
        public event Action<float[], bool, bool, string?>? OnMixedAudioDataAvailable;

        public event Action<AudioMixerStats>? OnMixingStatsUpdated;

        public bool IsInitialized => _initialized;
        public int OutputSampleRate => _outputSampleRate;
        public int OutputChannels => _outputChannels;
        public int FrameDuration => _frameDuration;

        #endregion

        #region Constructor

        public FFmpegAudioMixer(ILogger<FFmpegAudioMixer> logger)
        {
            _logger = logger;
            _audioStreams = new Dictionary<AudioType, AudioStreamProcessor>();
            // _audioFifos = new Dictionary<AudioType, IntPtr>();
            _sourceClosedStates = new Dictionary<AudioType, bool>();
            _sourceFilterCtxs = new Dictionary<AudioType, IntPtr>();
            _volumeStates = new Dictionary<AudioType, VolumeTransitionControl>();
            _volumeLevels = new Dictionary<AudioType, float>();
            _priorities = new Dictionary<AudioType, int>();

            // Initialize default configuration for audio types
            InitializeAudioTypes();

            // Initialize FFmpeg log level
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);
        }

        #endregion

        #region Output buffering and smooth delivery

        private void EmitMixedAudio(float[] mixedData, bool isFirst, bool isLast, string? sentenceId = null)
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
                        _logger.LogDebug("Buffer pacing baseline set with prefill delay {delay}ms", initialDelay);
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
                            _logger.LogDebug("Prefilled {count} frames. Starting pacing timer.", _outputBuffer.Count);
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

            // If we are waiting to emit the session-ending last frame, do it only after
            // all queued audio has been played out.
            if (_pendingFinalLastFrame && !_lastFrameEmitted)
            {
                bool canEmit;
                lock (_bufferLock)
                {
                    canEmit = _outputBuffer.Count == 0;
                }

                if (canEmit)
                {
                    var silentFrame = new float[_frameSampleCount];
                    EmitMixedAudio(silentFrame, false, true, null);
                    _lastFrameEmitted = true;
                    _pendingFinalLastFrame = false;
                }
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


        #endregion

        #region Initialization and configuration

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

        private void UpdateVolumeLevelsFromConfig()
        {
            _volumeLevels[AudioType.SystemNotification] = _config.SystemNotificationVolumeConfig.BaseVolume;
            _volumeLevels[AudioType.TTS] = _config.TTSVolumeConfig.BaseVolume;
            _volumeLevels[AudioType.Music] = _config.MusicVolumeConfig.BaseVolume;
        }

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
                    // Use packed float format for easy interop with managed float[]
                    _sampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
                    _channelLayout = outputChannels == 2 ? ffmpeg.AV_CH_LAYOUT_STEREO : ffmpeg.AV_CH_LAYOUT_MONO;

                    // Update volume configuration
                    UpdateVolumeLevelsFromConfig();

                    _initialized = true;
                    SetState(AudioMixerState.Idle);

                    _logger.LogInformation("FFmpeg audio mixer initialized successfully with SampleRate={SampleRate}, Channels={Channels}, FrameDuration={FrameDuration}ms",
                        outputSampleRate, outputChannels, frameDuration);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize FFmpeg audio mixer");
                    return false;
                }
            }
        }

        #endregion

        #region Audio stream management

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

            lock (_streamLock)
            {
                // Get or create audio stream processor
                if (!_audioStreams.TryGetValue(audioType, out var streamProcessor))
                {
                    streamProcessor = CreateAudioStreamProcessor(audioType);
                    _audioStreams[audioType] = streamProcessor;
                    _totalStreamCount++;
                    _filterGraphDirty = true;
                }

                // Add data to the stream processor (for tracking purposes)
                streamProcessor.AddData(audioData, sentenceId);

                // WriteToAudioFifo removed - we now use AudioStreamProcessor as the buffer

                // Reset last-frame flag since new data arrived
                _lastFrameEmitted = false;

                // Mark data available
                _dataAvailableEvent.Set();

                // Start mixing process
                if (!_isMixing)
                {
                    StartMixingProcess();
                }
            }
        }

        public void StopAudioStream(AudioType audioType)
        {
            lock (_streamLock)
            {
                if (_audioStreams.TryGetValue(audioType, out var streamProcessor))
                {
                    streamProcessor.Stop();
                    _logger.LogDebug("Marked audio stream {AudioType} for stopping", audioType);
                }
            }
        }

        private AudioStreamProcessor CreateAudioStreamProcessor(AudioType audioType)
        {
            var processor = new AudioStreamProcessor(audioType, _outputSampleRate, _outputChannels, _frameDuration, _config);

            // FIFO allocation removed - using AudioStreamProcessor buffer directly
            
            _sourceClosedStates[audioType] = false;

            _logger.LogDebug("Created audio stream processor for {AudioType}", audioType);
            return processor;
        }
        #endregion

        #region Filter graph management

        private void ReconfigureFilterGraph()
        {
            try
            {
                lock (_filterLock)
                {
                    CleanupFilterGraph();
                    CreateFilterGraph();
                }
                _logger.LogDebug("Filter graph reconfigured with {StreamCount} streams", _audioStreams.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconfigure filter graph");
            }
        }

        private void CreateFilterGraph()
        {
            if (_audioStreams.Count == 0)
                return;

            _filterGraph = ffmpeg.avfilter_graph_alloc();
            if (_filterGraph == null)
            {
                throw new InvalidOperationException("Failed to allocate filter graph");
            }

            // Create output sink filter
            CreateSinkFilter();

            // Create a source filter for each audio stream
            foreach (var audioType in _audioStreams.Keys)
            {
                CreateSourceFilter(audioType);
            }

            // Configure filter graph
            ConfigureFilterGraph();
        }

        private void CreateSinkFilter()
        {
            var sinkFilter = ffmpeg.avfilter_get_by_name("abuffersink");
            if (sinkFilter == null)
            {
                throw new InvalidOperationException("Failed to get abuffersink filter");
            }

            AVFilterContext* sinkCtx = null;
            var ret = ffmpeg.avfilter_graph_create_filter(&sinkCtx, sinkFilter, "out", null, null, _filterGraph);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to create sink filter: {ret.FFErrorToText()}");
            }
            _sinkFilterCtx = sinkCtx;

            // Set output format parameters
            SetSinkFilterParameters();
        }

        private void SetSinkFilterParameters()
        {
            if (_sinkFilterCtx == null)
            {
                _logger.LogWarning("Sink filter context is null when setting parameters");
                return;
            }

            _logger.LogDebug("Sink filter parameters target: {SampleRate}Hz, {Channels}ch, {Format}",
                _outputSampleRate, _outputChannels, ffmpeg.av_get_sample_fmt_name(_sampleFormat));
        }

        private void CreateSourceFilter(AudioType audioType)
        {
            var sourceFilter = ffmpeg.avfilter_get_by_name("abuffer");
            if (sourceFilter == null)
            {
                throw new InvalidOperationException("Failed to get abuffer filter");
            }

            var filterName = $"in{(int)audioType}";
            var args = $"time_base=1/{_outputSampleRate}:sample_rate={_outputSampleRate}:" +
                      $"sample_fmt={ffmpeg.av_get_sample_fmt_name(_sampleFormat)}:channel_layout=0x{_channelLayout:X}";

            AVFilterContext* sourceCtx = null;
            var ret = ffmpeg.avfilter_graph_create_filter(&sourceCtx, sourceFilter, filterName, args, null, _filterGraph);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to create source filter for {audioType}: {ret.FFErrorToText()}");
            }

            _sourceFilterCtxs[audioType] = (IntPtr)sourceCtx;
        }

        private void ConfigureFilterGraph()
        {
            if (_audioStreams.Count == 1)
            {
                // Single input stream, direct connection
                var first = _sourceFilterCtxs.Values.First();
                var sourceCtx = (AVFilterContext*)first;
                var ret = ffmpeg.avfilter_link(sourceCtx, 0u, _sinkFilterCtx, 0u);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"Failed to link single source to sink: {ret.FFErrorToText()}");
                }
            }
            else
            {
                // Multiple input streams, use amix filter
                CreateAmixFilter();
            }

            // Configure filter graph
            var configRet = ffmpeg.avfilter_graph_config(_filterGraph, null);
            if (configRet < 0)
            {
                throw new InvalidOperationException($"Failed to configure filter graph: {configRet.FFErrorToText()}");
            }
        }

        private void CreateAmixFilter()
        {
            var amixFilter = ffmpeg.avfilter_get_by_name("amix");
            if (amixFilter == null)
            {
                throw new InvalidOperationException("Failed to get amix filter");
            }

            var inputCount = _audioStreams.Count;
            var args = $"inputs={inputCount}";

            AVFilterContext* amixCtx = null;
            var ret = ffmpeg.avfilter_graph_create_filter(&amixCtx, amixFilter, "amix", args, null, _filterGraph);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to create amix filter: {ret.FFErrorToText()}");
            }

            // Connect all input sources to amix
            uint inputIndex = 0;
            foreach (var src in _sourceFilterCtxs.Values)
            {
                var sourceCtx = (AVFilterContext*)src;
                ret = ffmpeg.avfilter_link(sourceCtx, 0u, amixCtx, inputIndex++);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"Failed to link source to amix: {ret.FFErrorToText()}");
                }
            }

            // Link amix to sink
            ret = ffmpeg.avfilter_link(amixCtx, 0u, _sinkFilterCtx, 0u);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to link amix to sink: {ret.FFErrorToText()}");
            }
        }

        private void CleanupFilterGraph()
        {
            if (_filterGraph != null)
            {
                var graph = _filterGraph;
                ffmpeg.avfilter_graph_free(&graph);
                _filterGraph = null;
            }

            _sourceFilterCtxs.Clear();
            _sinkFilterCtx = null;
        }

        private void CleanupCompletedStreams()
        {
            var streamsToRemove = new List<AudioType>();

            foreach (var kvp in _audioStreams.ToList())
            {
                var audioType = kvp.Key;
                var streamProcessor = kvp.Value;

                if (streamProcessor.IsStopping)
                {
                    if (!streamProcessor.HasAnyData())
                    {
                        // Close source filter
                        var isSourceClosed = _sourceClosedStates.GetValueOrDefault(audioType, false);
                        if (_sourceFilterCtxs.TryGetValue(audioType, out var srcPtr) && !isSourceClosed)
                        {
                            try
                            {
                                var sourceCtx = (AVFilterContext*)srcPtr;
                                var ret = ffmpeg.av_buffersrc_close(sourceCtx, _pts, 0);
                                if (ret >= 0)
                                {
                                    _sourceClosedStates[audioType] = true;
                                    _logger.LogDebug("Closed source filter for {AudioType}", audioType);
                                }
                                else
                                {
                                    _logger.LogWarning("Failed to close source filter for {AudioType}: {Error}",
                                        audioType, ret.FFErrorToText());
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Exception closing source filter for {AudioType}", audioType);
                            }
                        }

                        // notify only once when the stream is fully done
                        
                        streamsToRemove.Add(audioType);

                        _logger.LogDebug("Stream {AudioType} completed and will be removed", audioType);
                    }
                }
            }

            // Remove completed streams
            foreach (var audioType in streamsToRemove)
            {
                RemoveAudioStream(audioType);
            }
        }

        private void RemoveAudioStream(AudioType audioType)
        {
            _logger.LogDebug("Removing audio stream {AudioType}", audioType);

            // Remove and dispose audio stream processor
            if (_audioStreams.TryGetValue(audioType, out var streamProcessor))
            {
                streamProcessor.Dispose();
                _audioStreams.Remove(audioType);
            }

            // FIFO cleanup removed

            // Remove source closed state
            _sourceClosedStates.Remove(audioType);

            // Remove source reference
            _sourceFilterCtxs.Remove(audioType);
            _completedStreamCount++;

            // Check if there are still active streams
            _filterGraphDirty = true;

            var activeStreams = _audioStreams.Where(kvp => !kvp.Value.IsComplete && !kvp.Value.IsStopping).ToList();

            if (activeStreams.Count == 0)
            {
                _logger.LogDebug("No more active streams, will enter draining phase after delay");
                _draining = true;

                // We reached end-of-session (no active streams). Ensure we will emit exactly one
                // last-frame AFTER all already enqueued audio has played out.
                // This avoids the sender stopping early while also guaranteeing the session ends.
                if (!_lastFrameEmitted)
                {
                    _pendingFinalLastFrame = true;
                    _dataAvailableEvent.Set();
                }

                // Delay a little before fully stopping, allowing other streams to continue
                _ = Task.Delay(100).ContinueWith(_ =>
                {
                    if (_audioStreams.Count == 0 && _draining)
                    {
                        _logger.LogDebug("All streams completed, gracefully ending mixer");
                      }
                });
            }
            else
            {
                _logger.LogDebug("Still have {ActiveCount} active streams, continuing", activeStreams.Count);
            }
        }

        #region Statistics and state management

        private void UpdateStatistics(float[] audioData)
        {
            float sumSquares = 0;
            float peak = 0;

            for (int i = 0; i < audioData.Length; i++)
            {
                var sample = Math.Abs(audioData[i]);
                sumSquares += audioData[i] * audioData[i];
                if (sample > peak) peak = sample;
            }

            _currentStats.CurrentRms = (float)Math.Sqrt(sumSquares / audioData.Length);
            _currentStats.CurrentPeak = peak;
            _currentStats.CurrentGainDb = 20 * (float)Math.Log10(Math.Max(_currentStats.CurrentRms, 1e-10f));
            _currentStats.ActiveStreamCount = _audioStreams.Count(kvp => !kvp.Value.IsComplete);

            OnMixingStatsUpdated?.Invoke(_currentStats);
        }

        private void SetState(AudioMixerState newState)
        {
            if (_currentState != newState)
            {
                _currentState = newState;
                if (newState == AudioMixerState.Mixing)
                {
                    _firstFrameAfterStart = true;
                    _lastFrameEmitted = false; // reset last-frame flag on start
                }
                else
                {
                    _firstFrameAfterStart = false;
                }
                OnStateChanged?.Invoke(newState);
                _logger.LogDebug("FFmpeg audio mixer state changed to {State}", newState);
            }
        }

        public AudioMixerStats GetCurrentStats()
        {
            return new AudioMixerStats
            {
                CurrentRms = _currentStats.CurrentRms,
                CurrentPeak = _currentStats.CurrentPeak,
                CurrentGainDb = _currentStats.CurrentGainDb,
                ActiveStreamCount = _currentStats.ActiveStreamCount,
                DelayCompensation = new Dictionary<AudioType, float>()
            };
        }

        #endregion

        #region Utility methods

        private VolumeTransitionControl GetOrCreateVolumeState(AudioType audioType)
        {
            if (!_volumeStates.TryGetValue(audioType, out var volumeState))
            {
                volumeState = new VolumeTransitionControl();
                _volumeStates[audioType] = volumeState;
            }
            return volumeState;
        }

        private void ApplyAudioProcessingEnhancements(float[] audioData)
        {
            if (audioData == null || audioData.Length == 0)
                return;

            // Apply smooth normalization
            ApplySmoothNormalization(audioData, _audioStreams.Count > 1);

            // Apply dynamic gain control
            ApplyDynamicGainControlSmooth(audioData, _audioStreams.Count > 1);

            // Apply enhanced limiter
            ApplyEnhancedLimiting(audioData);
        }

        private void ApplySmoothNormalization(float[] audioData, bool isMultiStream)
        {
            if (audioData.Length == 0) return;

            // Calculate current audio RMS
            float rmsSum = 0;
            for (int i = 0; i < audioData.Length; i++)
            {
                float sample = audioData[i];
                rmsSum += sample * sample;
            }
            float rms = (float)Math.Sqrt(rmsSum / audioData.Length);

            if (rms > 0.005f) // Avoid normalizing very small signals
            {
                float targetFactor;
                if (isMultiStream)
                {
                    // Use a more conservative normalization for multiple streams
                    int activeCount = _audioStreams.Count(kvp => !kvp.Value.IsComplete);
                    targetFactor = (float)(0.75 / Math.Sqrt(Math.Max(activeCount, 1)));
                }
                else
                {
                    // Use standard normalization for single stream
                    targetFactor = 0.75f;
                }

                // Smooth normalization changes
                _lastNormalizationFactor = SmoothNormalization(_lastNormalizationFactor, targetFactor);

                // Apply normalization
                for (int i = 0; i < audioData.Length; i++)
                {
                    audioData[i] *= _lastNormalizationFactor;
                }
            }
        }

        private static float SmoothNormalization(float previous, float target)
        {
            // Limit per-frame change to avoid abrupt gain changes
            float maxStepUp = 0.05f;   // allow small increases
            float maxStepDown = 0.15f; // allow moderate decreases
            float delta = target - previous;
            if (delta > maxStepUp) delta = maxStepUp;
            else if (delta < -maxStepDown) delta = -maxStepDown;
            return previous + delta;
        }

        private void ApplyDynamicGainControlSmooth(float[] audioData, bool isMultiStream)
        {
            float rmsSum = 0;
            for (int i = 0; i < audioData.Length; i++)
            {
                float sample = audioData[i];
                rmsSum += sample * sample;
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

                    float sign = audioData[i] >= 0 ? 1.0f : -1.0f;
                    audioData[i] = sign * newLevel;
                    _currentStats.LimiterTriggerCount++;
                }
            }
        }

        #endregion

        private float[]? ConvertFrameToFloatArrayInternal(AVFrame* frame)
        {
            try
            {
                var sampleCount = frame->nb_samples * _outputChannels;
                var result = new float[sampleCount];
                Marshal.Copy((IntPtr)frame->data[0], result, 0, sampleCount);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting frame to float array");
                return null;
            }
        }

        #endregion

        #region Mixing process

        private void StartMixingProcess()
        {
            if (_isMixing || _disposed)
                return;

            _isMixing = true;
            _shouldStop = false;
            _hasEmittedFirst = false;
            _hasEmittedLast = false;
            _draining = false;
            _pendingFinalLastFrame = false;
            SetState(AudioMixerState.Mixing);

            // Reset pacing/buffering state for a new mixing session.
            // Otherwise a previous session may have left the playback timer running or
            // the buffer marked as prefilled, causing incorrect output pacing and end-frame ordering.
            lock (_bufferLock)
            {
                _outputBuffer.Clear();
                _bufferPreFilled = false;
                _playbackStarted = false;
                _lastScheduledOutputTime = DateTime.MinValue;
            }
            _playbackTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _playbackTimer?.Dispose();
            _playbackTimer = null;

            _mixingThread = new Thread(MixingThreadProc)
            {
                Name = $"FFmpegAudioMixer-{GetHashCode()}",
                IsBackground = true
            };
            _mixingThread.Start();

            _logger.LogDebug("Started mixing process");
        }

        private void MixingThreadProc()
        {
            try
            {
                while (!_shouldStop && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (!ProcessMixing())
                    {
                        // If no active streams and last frame has not been emitted, try draining
                        if (_audioStreams.Count == 0 && !_hasEmittedLast && _filterGraph != null)
                        {
                            DrainSinkAndEmitLast();
                        }

                        // Check if all streams are completed and we should stop the thread
                        bool shouldStopThread = false;
                        lock (_streamLock)
                        {
                            if (_audioStreams.Count == 0 && _lastFrameEmitted)
                            {
                                shouldStopThread = true;
                            }
                        }

                        if (shouldStopThread)
                        {
                            _logger.LogDebug("All streams completed, stopping mixing thread to allow restart for next session");
                            break;
                        }

                        // Wait for data or check whether we should stop
                        _dataAvailableEvent.Wait(TimeSpan.FromMilliseconds(50), _cancellationTokenSource.Token);
                        _dataAvailableEvent.Reset();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Mixing thread cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in mixing thread");
            }
            finally
            {
                _isMixing = false;
                SetState(AudioMixerState.Idle);
                _logger.LogDebug("Mixing thread stopped");
            }
        }

        private bool ProcessMixing()
        {
            lock (_streamLock)
            {
                if (_filterGraphDirty)
                {
                    _filterGraphDirty = false;
                    ReconfigureFilterGraph();
                }

                if (_filterGraph == null)
                    return false;

                bool hasProcessedData = false;

                // Check whether the output buffer is full
                lock (_bufferLock)
                {
                    if (_outputBuffer.Count >= Math.Max(1, _config.MaxOutputBufferFrames))
                    {
                        return false; // Buffer full, pause processing
                    }
                }

                // Get active streams with improved buffer strategy
                var activeStreams = GetActiveStreamsWithBufferStrategy();

                string? batchSentenceId = null;

                // Process each active audio stream
                foreach (var (audioType, streamProcessor) in activeStreams)
                {
                    // Read from buffer and push to filter
                    var (processed, sentenceId) = ProcessAudioStream(audioType, streamProcessor);
                    if (processed)
                    {
                        hasProcessedData = true;
                    }

                    // Priority logic for sentence ID (TTS > others)
                    if (!string.IsNullOrEmpty(sentenceId))
                    {
                        if (audioType == AudioType.TTS)
                        {
                            batchSentenceId = sentenceId;
                        }
                        else if (batchSentenceId == null)
                        {
                            batchSentenceId = sentenceId;
                        }
                    }
                }

                bool metaOnlyEmitted = false;
                if (hasProcessedData)
                {
                    lock (_bufferLock)
                    {
                        _pendingSentenceIds.Enqueue(batchSentenceId);
                    }
                }
                else if (batchSentenceId != null)
                {
                    // No audio processed, but we have a sentenceId. Emit empty frame.
                    bool isFirst = _firstFrameAfterStart;
                    if (_firstFrameAfterStart) _firstFrameAfterStart = false;

                    EmitMixedAudio(Array.Empty<float>(), isFirst, false, batchSentenceId);
                    metaOnlyEmitted = true;
                }

                // Retrieve mixed audio data from the filter graph
                bool hasActiveTransitions = _volumeStates.Values.Any(v => v.IsTransitioning);
                if (hasProcessedData || hasActiveTransitions)
                {
                    RetrieveMixedAudioData();
                }
                else if (_audioStreams.Count > 0)
                {
                    // If there are streams but no processed data, we may need to send silent frames to keep continuity
                    bool hasActiveStreams = _audioStreams.Values.Any(s => !s.IsComplete && !s.IsStopping);
                    if (hasActiveStreams && !metaOnlyEmitted)
                    {
                        // Send a short silent frame to maintain audio continuity
                        var silentFrame = new float[_frameSampleCount];
                        bool isFirst = _firstFrameAfterStart;
                        if (_firstFrameAfterStart) _firstFrameAfterStart = false;

                        EmitMixedAudio(silentFrame, isFirst, false, null);
                        _logger.LogTrace("Emitted silence frame to maintain audio continuity");

                    }
                }

                // Cleanup completed streams
                CleanupCompletedStreams();

                return hasProcessedData || hasActiveTransitions || metaOnlyEmitted;
            }
        }

        private List<(AudioType type, AudioStreamProcessor processor)> GetActiveStreamsWithBufferStrategy()
        {
            var activeStreams = new List<(AudioType, AudioStreamProcessor)>();

            foreach (var kvp in _audioStreams)
            {
                var audioType = kvp.Key;
                var streamProcessor = kvp.Value;

                if (streamProcessor.IsComplete)
                    continue;

                // FIFO check removed
                // if (!_audioFifos.TryGetValue(audioType, out var fifoPtr))
                //    continue;

                // var fifo = (AVAudioFifo*)fifoPtr;
                var availableSamples = streamProcessor.AvailableDataCount; // ffmpeg.av_audio_fifo_size(fifo);
                var requiredSamples = _frameSampleCount; // / _outputChannels; // AvailableDataCount is total samples (floats)

                bool hasFullFrame = availableSamples >= requiredSamples;
                bool isNewStream = streamProcessor.ProcessedFrameCount < 3;

                if (hasFullFrame)
                {
                    activeStreams.Add((audioType, streamProcessor));
                }
                else if (availableSamples == 0 && streamProcessor.HasAnyData())
                {
                    // Handle meta-only frames
                    activeStreams.Add((audioType, streamProcessor));
                }
                else if (isNewStream && _config.EnableSmoothVolumeControl)
                {
                    // Allow partial frame on new streams to reduce startup latency
                    int minRequiredSamples = (int)(requiredSamples * _config.NewStreamBufferTolerance);
                    if (availableSamples >= minRequiredSamples)
                    {
                        activeStreams.Add((audioType, streamProcessor));
                    }
                }
                else if (streamProcessor.IsStopping && availableSamples > 0)
                {
                    activeStreams.Add((audioType, streamProcessor));
                }
            }

            return activeStreams;
        }

        private (bool processed, string? sentenceId) ProcessAudioStream(AudioType audioType, AudioStreamProcessor streamProcessor)
        {
            if (!_sourceFilterCtxs.TryGetValue(audioType, out var sourceCtxPtr))
                return (false, null);

            var sourceCtx = (AVFilterContext*)sourceCtxPtr;

            var availableSamples = streamProcessor.AvailableDataCount;
            var requiredSamples = _frameSampleCount; // Total samples

            // Use improved buffering strategy
            bool hasFullFrame = availableSamples >= requiredSamples;
            bool isNewStream = streamProcessor.ProcessedFrameCount < 3;
            bool canProcess = false;

            if (hasFullFrame)
            {
                canProcess = true;
            }
            else if (availableSamples == 0 && streamProcessor.HasAnyData())
            {
                canProcess = true;
            }
            else if (isNewStream && _config.EnableSmoothVolumeControl)
            {
                int minRequiredSamples = (int)(requiredSamples * _config.NewStreamBufferTolerance);
                canProcess = availableSamples >= minRequiredSamples;
            }
            else if (streamProcessor.IsStopping && availableSamples > 0)
            {
                canProcess = true;
            }

            if (!canProcess)
                return (false, null);

            try
            {
                // Get data from processor
                int samplesRead;
                string? sentenceId;
                var audioData = streamProcessor.GetFrameDataWithPartialSupport(_frameSampleCount, out samplesRead, out sentenceId);

                if (audioData == null || audioData.Length == 0)
                {
                    return (false, sentenceId);
                }

                // Apply volume control
                var volumeState = GetOrCreateVolumeState(audioType);
                var targetVolume = _volumeLevels.GetValueOrDefault(audioType, 0.5f);

                float currentVolume;
                if (!_config.EnableSmoothVolumeControl)
                {
                    currentVolume = targetVolume;
                }
                else
                {
                    const float eps = 0.0001f;
                    if (!volumeState.IsTransitioning && Math.Abs(volumeState.TargetVolume - targetVolume) > eps)
                    {
                        volumeState.StartTransition(targetVolume, _config.VolumeTransitionDurationMs, _config.TransitionCurve);
                    }
                    currentVolume = volumeState.UpdateAndGetCurrentVolume();
                }

                // Apply volume
                for (int i = 0; i < audioData.Length; i++)
                {
                    audioData[i] *= currentVolume;
                }

                // Create audio frame
                var frame = ffmpeg.av_frame_alloc();
                if (frame == null)
                    return (false, null);

                // Set frame parameters
                frame->nb_samples = audioData.Length / _outputChannels;

                AVChannelLayout ch;
                ffmpeg.av_channel_layout_from_mask(&ch, _channelLayout);
                frame->ch_layout = ch;
                frame->format = (int)_sampleFormat;
                frame->sample_rate = _outputSampleRate;
                frame->pts = _pts;

                // Allocate frame buffer
                var ret = ffmpeg.av_frame_get_buffer(frame, 0);
                if (ret < 0)
                {
                    ffmpeg.av_frame_free(&frame);
                    return (false, null);
                }

                // Copy data to frame (packed format)
                Marshal.Copy(audioData, 0, (IntPtr)frame->data[0], audioData.Length);

                int nbSamples = frame->nb_samples;

                // push the frame to the filter
                ret = ffmpeg.av_buffersrc_add_frame_flags(sourceCtx, frame, 0);
                ffmpeg.av_frame_free(&frame);

                if (ret < 0)
                {
                    _logger.LogError("Failed to add frame to filter for {AudioType}: {Error}",
                        audioType, ret.FFErrorToText());
                    return (false, null);
                }

                streamProcessor.MarkFrameProcessed();

                _pts += nbSamples;

                return (true, sentenceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio stream {AudioType}", audioType);
                return (false, null);
            }
        }

        // WriteToAudioFifo removed - we now use AudioStreamProcessor as the buffer

        private void RetrieveMixedAudioData()
        {
            if (_sinkFilterCtx == null)
                return;

            try
            {
                var frame = ffmpeg.av_frame_alloc();
                if (frame == null)
                    return;

                var ret = ffmpeg.av_buffersink_get_frame(_sinkFilterCtx, frame);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                {
                    ffmpeg.av_frame_free(&frame);

                    // If there is no available frame but we have active volume transitions, send a silent frame
                    if (_volumeStates.Values.Any(v => v.IsTransitioning))
                    {
                        foreach (var v in _volumeStates.Values)
                        {
                            _ = v.UpdateAndGetCurrentVolume();
                        }

                        var silentFrame = new float[_frameSampleCount];
                        bool isFirst = _firstFrameAfterStart;
                        if (_firstFrameAfterStart) _firstFrameAfterStart = false;

                        EmitMixedAudio(silentFrame, isFirst, false, null);
                    }
                    return;
                }

                if (ret < 0)
                {
                    _logger.LogError("Failed to get frame from sink filter: {Error}", ret.FFErrorToText());
                    ffmpeg.av_frame_free(&frame);
                    return;
                }

                var mixedData = ConvertFrameToFloatArrayInternal(frame);
                ffmpeg.av_frame_free(&frame);

                if (mixedData != null && mixedData.Length > 0)
                {
                    // Apply audio processing improvements
                    ApplyAudioProcessingEnhancements(mixedData);

                    // Update statistics
                    UpdateStatistics(mixedData);

                    // Determine whether this is the first and the last frame
                    bool isFirst = _firstFrameAfterStart;
                    if (_firstFrameAfterStart) _firstFrameAfterStart = false;

                    string? sentenceId = null;
                    lock (_bufferLock)
                    {
                        if (_pendingSentenceIds.Count > 0)
                        {
                            sentenceId = _pendingSentenceIds.Dequeue();
                        }
                    }

                    // IMPORTANT: do NOT mark last-frame here.
                    // FFmpeg filter output may still have buffered audio and/or the output pacing buffer
                    // may still contain frames. Marking last here can cause the sender to stop early.
                    EmitMixedAudio(mixedData, isFirst, false, sentenceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving mixed audio data");
            }
        }

        private bool IsFullyDrainedForLastFrame()
        {
            // At end-of-session we remove streams and free FIFOs. So the reliable signal is:
            // no active stream processors, no FIFOs left, and no queued output frames.
            if (_audioStreams.Count != 0) return false;
            // if (_audioFifos.Count != 0) return false;
            lock (_bufferLock) return _outputBuffer.Count == 0;
        }

        private void DrainSinkAndEmitLast()
        {
            if (_sinkFilterCtx == null || _hasEmittedLast || _lastFrameEmitted)
                return;

            try
            {

                while (true)
                {
                    var frame = ffmpeg.av_frame_alloc();
                    if (frame == null) break;

                    var ret = ffmpeg.av_buffersink_get_frame(_sinkFilterCtx, frame);
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        ffmpeg.av_frame_free(&frame);
                        break;
                    }
                    if (ret == ffmpeg.AVERROR_EOF)
                    {
                        ffmpeg.av_frame_free(&frame);
                        break;
                    }
                    if (ret < 0)
                    {
                        ffmpeg.av_frame_free(&frame);
                        break;
                    }

                    var mixedData = ConvertFrameToFloatArrayInternal(frame);
                    ffmpeg.av_frame_free(&frame);

                    if (mixedData != null && mixedData.Length > 0)
                    {
                        UpdateStatistics(mixedData);

                        bool isFirst = _firstFrameAfterStart;
                        if (_firstFrameAfterStart) _firstFrameAfterStart = false;

                        string? sentenceId = null;
                        lock (_bufferLock)
                        {
                            if (_pendingSentenceIds.Count > 0)
                            {
                                sentenceId = _pendingSentenceIds.Dequeue();
                            }
                        }

                        EmitMixedAudio(mixedData, isFirst, false, sentenceId);
                    }
                }

                // Send a final silent frame to indicate the end
                // We can't emit last-frame until all already-enqueued audio has played out.
                // Here we only schedule it; PlaybackTimerCallback will emit it once the output queue is empty.
                if (!_lastFrameEmitted && !_pendingFinalLastFrame && IsFullyDrainedForLastFrame())
                {
                    _pendingFinalLastFrame = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error draining sink");
            }
        }

        public void ClearAllBuffers()
        {
            lock (_streamLock)
            {
                // Dispose and clear all streams to ensure mixer stops
                foreach (var streamProcessor in _audioStreams.Values)
                {
                    streamProcessor.ClearBuffer();
                }
                _audioStreams.Clear();
                _sourceClosedStates.Clear();
                _volumeStates.Clear();

                // clear output buffer
                lock (_bufferLock)
                {
                    _outputBuffer.Clear();
                    _pendingSentenceIds.Clear();
                    _bufferPreFilled = false;
                }

                // Force filter graph recreation to flush internal buffers
                _filterGraphDirty = true;

                // Reset state flags
                _pendingFinalLastFrame = false;
                _draining = false;
                _lastFrameEmitted = true; // Allow thread to exit
                _firstFrameAfterStart = true;

                // Wake up the thread so it can check the stop condition
                _dataAvailableEvent.Set();
                
                _logger.LogDebug("Cleared all audio buffers and streams");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_filterLock)
            {
                _shouldStop = true;
                _cancellationTokenSource.Cancel();

                // Send remaining frames in the output buffer before cleanup
                lock (_bufferLock)
                {
                    while (_outputBuffer.Count > 0)
                    {
                        var frame = _outputBuffer.Dequeue();
                        OutputAudioData(frame.Data, frame.IsFirst, frame.IsLast, frame.SentenceId);
                    }
                }

                // Wait for mixing thread to finish
                _mixingThread?.Join(TimeSpan.FromSeconds(5));

                // Stop the playback timer
                _playbackTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _playbackTimer?.Dispose();

                // Cleanup FFmpeg resources
                CleanupFilterGraph();

                // FIFO free removed
                // foreach (var fifoPtr in _audioFifos.Values)
                // {
                //     ffmpeg.av_audio_fifo_free((AVAudioFifo*)fifoPtr);
                // }
                // _audioFifos.Clear();

                // Dispose all stream processors
                foreach (var streamProcessor in _audioStreams.Values)
                {
                    streamProcessor.Dispose();
                }
                _audioStreams.Clear();
                _sourceClosedStates.Clear();

                _dataAvailableEvent.Dispose();
                _cancellationTokenSource.Dispose();

                _disposed = true;
                _initialized = false;
            }

            _logger.LogInformation("FFmpeg audio mixer disposed");
        }

        #endregion
    }
}