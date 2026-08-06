using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
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
        private int _frameSampleCount;
        private AVSampleFormat _sampleFormat;
        private ulong _channelLayout;

        // Configuration and state management
        private AudioMixerConfig _config = new();
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
        private readonly AudioMixerStats _currentStats = new();
        private long _pts = 0;
        private int _totalStreamCount = 0;
        private int _completedStreamCount = 0;

        // Output event flags
        private bool _hasEmittedFirst = false;
        private bool _hasEmittedLast = false;
        private bool _draining = false;
        private volatile bool _filterGraphDirty = false;

        // Output buffering mechanism - new
        private readonly Queue<OutputBufferFrame> _outputBuffer = new();
        private readonly Queue<string?> _pendingSentenceIds = new();
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
        private struct OutputBufferFrame(float[] data, bool isFirst, bool isLast, string? sentenceId)
        {
            public float[] Data = data;
            public bool IsFirst = isFirst;
            public bool IsLast = isLast;
            public string? SentenceId = sentenceId;
        }


        #endregion

        #region Events and properties

        public event Action<AudioMixerState>? OnStateChanged;
        public event Action<float[], bool, bool, string?>? OnMixedAudioDataAvailable;

        public event Action<AudioMixerStats>? OnMixingStatsUpdated;

        public bool IsInitialized { get; private set; }
        public int OutputSampleRate { get; private set; }
        public int OutputChannels { get; private set; }
        public int FrameDuration { get; private set; }

        #endregion

        #region Constructor

        public FFmpegAudioMixer(ILogger<FFmpegAudioMixer> logger)
        {
            this._logger = logger;
            this._audioStreams = [];
            // _audioFifos = new Dictionary<AudioType, IntPtr>();
            this._sourceClosedStates = [];
            this._sourceFilterCtxs = [];
            this._volumeStates = [];
            this._volumeLevels = [];
            this._priorities = [];

            // Initialize default configuration for audio types
            this.InitializeAudioTypes();

            // Initialize FFmpeg log level
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);
        }

        #endregion

        #region Output buffering and smooth delivery

        private void EmitMixedAudio(float[] mixedData, bool isFirst, bool isLast, string? sentenceId = null)
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
                        this._logger.LogDebug("Buffer pacing baseline set with prefill delay {delay}ms", initialDelay);
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
                            this._logger.LogDebug("Prefilled {count} frames. Starting pacing timer.", this._outputBuffer.Count);
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

            // If we are waiting to emit the session-ending last frame, do it only after
            // all queued audio has been played out.
            if (this._pendingFinalLastFrame && !this._lastFrameEmitted)
            {
                bool canEmit;
                lock (this._bufferLock)
                {
                    canEmit = this._outputBuffer.Count == 0;
                }

                if (canEmit)
                {
                    var silentFrame = new float[this._frameSampleCount];
                    this.EmitMixedAudio(silentFrame, false, true, null);
                    this._lastFrameEmitted = true;
                    this._pendingFinalLastFrame = false;
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
                this._logger?.LogError(ex, "Error in OnMixedAudioDataAvailable callback");
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
                        this._volumeLevels[audioType] = 1.0f;
                        this._priorities[audioType] = 1;
                        break;
                    case AudioType.TTS:
                        this._volumeLevels[audioType] = 0.9f;
                        this._priorities[audioType] = 2;
                        break;
                    case AudioType.Music:
                        this._volumeLevels[audioType] = 0.3f;
                        this._priorities[audioType] = 3;
                        break;
                    default:
                        this._volumeLevels[audioType] = 0.5f;
                        this._priorities[audioType] = 4;
                        break;
                }
            }
        }

        private void UpdateVolumeLevelsFromConfig()
        {
            this._volumeLevels[AudioType.SystemNotification] = this._config.SystemNotificationVolumeConfig.BaseVolume;
            this._volumeLevels[AudioType.TTS] = this._config.TTSVolumeConfig.BaseVolume;
            this._volumeLevels[AudioType.Music] = this._config.MusicVolumeConfig.BaseVolume;
        }

        public bool Initialize(int outputSampleRate, int outputChannels, int frameDuration, AudioMixerConfig? config = null)
        {
            lock (this._filterLock)
            {
                try
                {
                    if (this.IsInitialized)
                    {
                        this._logger.LogWarning("FFmpegAudioMixer is already initialized");
                        return true;
                    }

                    if (config is not null)
                    {
                        this._config = config;
                    }

                    this.OutputSampleRate = outputSampleRate;
                    this.OutputChannels = outputChannels;
                    this.FrameDuration = frameDuration;
                    this._frameSampleCount = outputSampleRate * frameDuration / 1000 * outputChannels;
                    // Use packed float format for easy interop with managed float[]
                    this._sampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
                    this._channelLayout = outputChannels == 2 ? ffmpeg.AV_CH_LAYOUT_STEREO : ffmpeg.AV_CH_LAYOUT_MONO;

                    // Update volume configuration
                    this.UpdateVolumeLevelsFromConfig();

                    this.IsInitialized = true;
                    this.SetState(AudioMixerState.Idle);

                    this._logger.LogInformation("FFmpeg audio mixer initialized successfully with SampleRate={SampleRate}, Channels={Channels}, FrameDuration={FrameDuration}ms",
                        outputSampleRate, outputChannels, frameDuration);
                    return true;
                }
                catch (Exception ex)
                {
                    this._logger.LogError(ex, "Failed to initialize FFmpeg audio mixer");
                    return false;
                }
            }
        }

        #endregion

        #region Audio stream management

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

            lock (this._streamLock)
            {
                // Get or create audio stream processor
                if (!this._audioStreams.TryGetValue(audioType, out var streamProcessor))
                {
                    streamProcessor = this.CreateAudioStreamProcessor(audioType);
                    this._audioStreams[audioType] = streamProcessor;
                    this._totalStreamCount++;
                    this._filterGraphDirty = true;
                }

                // Add data to the stream processor (for tracking purposes)
                streamProcessor.AddData(audioData, sentenceId);

                // WriteToAudioFifo removed - we now use AudioStreamProcessor as the buffer

                // Reset last-frame flag since new data arrived
                this._lastFrameEmitted = false;

                // Mark data available
                this._dataAvailableEvent.Set();

                // Start mixing process
                if (!this._isMixing)
                {
                    this.StartMixingProcess();
                }
            }
        }

        public void StopAudioStream(AudioType audioType)
        {
            lock (this._streamLock)
            {
                if (this._audioStreams.TryGetValue(audioType, out var streamProcessor))
                {
                    streamProcessor.Stop();
                    this._logger.LogDebug("Marked audio stream {AudioType} for stopping", audioType);
                }
            }
        }

        private AudioStreamProcessor CreateAudioStreamProcessor(AudioType audioType)
        {
            var processor = new AudioStreamProcessor(audioType, this.OutputSampleRate, this.OutputChannels, this.FrameDuration, this._config);

            // FIFO allocation removed - using AudioStreamProcessor buffer directly

            this._sourceClosedStates[audioType] = false;

            this._logger.LogDebug("Created audio stream processor for {AudioType}", audioType);
            return processor;
        }
        #endregion

        #region Filter graph management

        private void ReconfigureFilterGraph()
        {
            try
            {
                lock (this._filterLock)
                {
                    this.CleanupFilterGraph();
                    this.CreateFilterGraph();
                }
                this._logger.LogDebug("Filter graph reconfigured with {StreamCount} streams", this._audioStreams.Count);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to reconfigure filter graph");
            }
        }

        private void CreateFilterGraph()
        {
            if (this._audioStreams.Count == 0)
            {
                return;
            }

            this._filterGraph = ffmpeg.avfilter_graph_alloc();
            if (this._filterGraph == null)
            {
                throw new InvalidOperationException("Failed to allocate filter graph");
            }

            // Create output sink filter
            this.CreateSinkFilter();

            // Create a source filter for each audio stream
            foreach (var audioType in this._audioStreams.Keys)
            {
                this.CreateSourceFilter(audioType);
            }

            // Configure filter graph
            this.ConfigureFilterGraph();
        }

        private void CreateSinkFilter()
        {
            var sinkFilter = ffmpeg.avfilter_get_by_name("abuffersink");
            if (sinkFilter == null)
            {
                throw new InvalidOperationException("Failed to get abuffersink filter");
            }

            AVFilterContext* sinkCtx = null;
            var ret = ffmpeg.avfilter_graph_create_filter(&sinkCtx, sinkFilter, "out", null, null, this._filterGraph);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to create sink filter: {ret.FFErrorToText()}");
            }
            this._sinkFilterCtx = sinkCtx;

            // Set output format parameters
            this.SetSinkFilterParameters();
        }

        private void SetSinkFilterParameters()
        {
            if (this._sinkFilterCtx == null)
            {
                this._logger.LogWarning("Sink filter context is null when setting parameters");
                return;
            }

            this._logger.LogDebug("Sink filter parameters target: {SampleRate}Hz, {Channels}ch, {Format}",
                this.OutputSampleRate, this.OutputChannels, ffmpeg.av_get_sample_fmt_name(this._sampleFormat));
        }

        private void CreateSourceFilter(AudioType audioType)
        {
            var sourceFilter = ffmpeg.avfilter_get_by_name("abuffer");
            if (sourceFilter == null)
            {
                throw new InvalidOperationException("Failed to get abuffer filter");
            }

            var filterName = $"in{(int)audioType}";
            var args = $"time_base=1/{this.OutputSampleRate}:sample_rate={this.OutputSampleRate}:" +
                      $"sample_fmt={ffmpeg.av_get_sample_fmt_name(this._sampleFormat)}:channel_layout=0x{this._channelLayout:X}";

            AVFilterContext* sourceCtx = null;
            var ret = ffmpeg.avfilter_graph_create_filter(&sourceCtx, sourceFilter, filterName, args, null, this._filterGraph);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to create source filter for {audioType}: {ret.FFErrorToText()}");
            }

            this._sourceFilterCtxs[audioType] = (IntPtr)sourceCtx;
        }

        private void ConfigureFilterGraph()
        {
            if (this._audioStreams.Count == 1)
            {
                // Single input stream, direct connection
                var first = this._sourceFilterCtxs.Values.First();
                var sourceCtx = (AVFilterContext*)first;
                var ret = ffmpeg.avfilter_link(sourceCtx, 0u, this._sinkFilterCtx, 0u);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"Failed to link single source to sink: {ret.FFErrorToText()}");
                }
            }
            else
            {
                // Multiple input streams, use amix filter
                this.CreateAmixFilter();
            }

            // Configure filter graph
            var configRet = ffmpeg.avfilter_graph_config(this._filterGraph, null);
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

            var inputCount = this._audioStreams.Count;
            var args = $"inputs={inputCount}";

            AVFilterContext* amixCtx = null;
            var ret = ffmpeg.avfilter_graph_create_filter(&amixCtx, amixFilter, "amix", args, null, this._filterGraph);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to create amix filter: {ret.FFErrorToText()}");
            }

            // Connect all input sources to amix
            uint inputIndex = 0;
            foreach (var src in this._sourceFilterCtxs.Values)
            {
                var sourceCtx = (AVFilterContext*)src;
                ret = ffmpeg.avfilter_link(sourceCtx, 0u, amixCtx, inputIndex++);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"Failed to link source to amix: {ret.FFErrorToText()}");
                }
            }

            // Link amix to sink
            ret = ffmpeg.avfilter_link(amixCtx, 0u, this._sinkFilterCtx, 0u);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to link amix to sink: {ret.FFErrorToText()}");
            }
        }

        private void CleanupFilterGraph()
        {
            if (this._filterGraph != null)
            {
                var graph = this._filterGraph;
                ffmpeg.avfilter_graph_free(&graph);
                this._filterGraph = null;
            }

            this._sourceFilterCtxs.Clear();
            this._sinkFilterCtx = null;
        }

        private void CleanupCompletedStreams()
        {
            var streamsToRemove = new List<AudioType>();

            foreach (var kvp in this._audioStreams.ToList())
            {
                var audioType = kvp.Key;
                var streamProcessor = kvp.Value;

                if (streamProcessor.IsStopping)
                {
                    if (!streamProcessor.HasAnyData())
                    {
                        // Close source filter
                        var isSourceClosed = this._sourceClosedStates.GetValueOrDefault(audioType, false);
                        if (this._sourceFilterCtxs.TryGetValue(audioType, out var srcPtr) && !isSourceClosed)
                        {
                            try
                            {
                                var sourceCtx = (AVFilterContext*)srcPtr;
                                var ret = ffmpeg.av_buffersrc_close(sourceCtx, this._pts, 0);
                                if (ret >= 0)
                                {
                                    this._sourceClosedStates[audioType] = true;
                                    this._logger.LogDebug("Closed source filter for {AudioType}", audioType);
                                }
                                else
                                {
                                    this._logger.LogWarning("Failed to close source filter for {AudioType}: {Error}",
                                        audioType, ret.FFErrorToText());
                                }
                            }
                            catch (Exception ex)
                            {
                                this._logger.LogWarning(ex, "Exception closing source filter for {AudioType}", audioType);
                            }
                        }

                        // notify only once when the stream is fully done

                        streamsToRemove.Add(audioType);

                        this._logger.LogDebug("Stream {AudioType} completed and will be removed", audioType);
                    }
                }
            }

            // Remove completed streams
            foreach (var audioType in streamsToRemove)
            {
                this.RemoveAudioStream(audioType);
            }
        }

        private void RemoveAudioStream(AudioType audioType)
        {
            this._logger.LogDebug("Removing audio stream {AudioType}", audioType);

            // Remove and dispose audio stream processor
            if (this._audioStreams.TryGetValue(audioType, out var streamProcessor))
            {
                streamProcessor.Dispose();
                this._audioStreams.Remove(audioType);
            }

            // FIFO cleanup removed

            // Remove source closed state
            this._sourceClosedStates.Remove(audioType);

            // Remove source reference
            this._sourceFilterCtxs.Remove(audioType);
            this._completedStreamCount++;

            // Check if there are still active streams
            this._filterGraphDirty = true;

            var activeStreams = this._audioStreams.Where(kvp => !kvp.Value.IsComplete && !kvp.Value.IsStopping).ToList();

            if (activeStreams.Count == 0)
            {
                this._logger.LogDebug("No more active streams, will enter draining phase after delay");
                this._draining = true;

                // We reached end-of-session (no active streams). Ensure we will emit exactly one
                // last-frame AFTER all already enqueued audio has played out.
                // This avoids the sender stopping early while also guaranteeing the session ends.
                if (!this._lastFrameEmitted)
                {
                    this._pendingFinalLastFrame = true;
                    this._dataAvailableEvent.Set();
                }

                // Delay a little before fully stopping, allowing other streams to continue
                _ = Task.Delay(100).ContinueWith(_ =>
                {
                    if (this._audioStreams.Count == 0 && this._draining)
                    {
                        this._logger.LogDebug("All streams completed, gracefully ending mixer");
                    }
                });
            }
            else
            {
                this._logger.LogDebug("Still have {ActiveCount} active streams, continuing", activeStreams.Count);
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
                if (sample > peak)
                {
                    peak = sample;
                }
            }

            this._currentStats.CurrentRms = (float)Math.Sqrt(sumSquares / audioData.Length);
            this._currentStats.CurrentPeak = peak;
            this._currentStats.CurrentGainDb = 20 * (float)Math.Log10(Math.Max(this._currentStats.CurrentRms, 1e-10f));
            this._currentStats.ActiveStreamCount = this._audioStreams.Count(kvp => !kvp.Value.IsComplete);

            OnMixingStatsUpdated?.Invoke(this._currentStats);
        }

        private void SetState(AudioMixerState newState)
        {
            if (this._currentState != newState)
            {
                this._currentState = newState;
                if (newState == AudioMixerState.Mixing)
                {
                    this._firstFrameAfterStart = true;
                    this._lastFrameEmitted = false; // reset last-frame flag on start
                }
                else
                {
                    this._firstFrameAfterStart = false;
                }
                OnStateChanged?.Invoke(newState);
                this._logger.LogDebug("FFmpeg audio mixer state changed to {State}", newState);
            }
        }

        public AudioMixerStats GetCurrentStats()
        {
            return new AudioMixerStats
            {
                CurrentRms = this._currentStats.CurrentRms,
                CurrentPeak = this._currentStats.CurrentPeak,
                CurrentGainDb = this._currentStats.CurrentGainDb,
                ActiveStreamCount = this._currentStats.ActiveStreamCount,
                DelayCompensation = []
            };
        }

        #endregion

        #region Utility methods

        private VolumeTransitionControl GetOrCreateVolumeState(AudioType audioType)
        {
            if (!this._volumeStates.TryGetValue(audioType, out var volumeState))
            {
                volumeState = new VolumeTransitionControl();
                this._volumeStates[audioType] = volumeState;
            }
            return volumeState;
        }

        private void ApplyAudioProcessingEnhancements(float[] audioData)
        {
            if (audioData == null || audioData.Length == 0)
            {
                return;
            }

            // Apply smooth normalization
            this.ApplySmoothNormalization(audioData, this._audioStreams.Count > 1);

            // Apply dynamic gain control
            this.ApplyDynamicGainControlSmooth(audioData, this._audioStreams.Count > 1);

            // Apply enhanced limiter
            this.ApplyEnhancedLimiting(audioData);
        }

        private void ApplySmoothNormalization(float[] audioData, bool isMultiStream)
        {
            if (audioData.Length == 0)
            {
                return;
            }

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
                    int activeCount = this._audioStreams.Count(kvp => !kvp.Value.IsComplete);
                    targetFactor = (float)(0.75 / Math.Sqrt(Math.Max(activeCount, 1)));
                }
                else
                {
                    // Use standard normalization for single stream
                    targetFactor = 0.75f;
                }

                // Smooth normalization changes
                this._lastNormalizationFactor = SmoothNormalization(this._lastNormalizationFactor, targetFactor);

                // Apply normalization
                for (int i = 0; i < audioData.Length; i++)
                {
                    audioData[i] *= this._lastNormalizationFactor;
                }
            }
        }

        private static float SmoothNormalization(float previous, float target)
        {
            // Limit per-frame change to avoid abrupt gain changes
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
                    float smoothedGain = 1.0f + ((gain - 1.0f) * 0.3f);
                    for (int i = 0; i < audioData.Length; i++)
                    {
                        audioData[i] *= smoothedGain;
                    }
                }
            }
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

                    float sign = audioData[i] >= 0 ? 1.0f : -1.0f;
                    audioData[i] = sign * newLevel;
                    this._currentStats.LimiterTriggerCount++;
                }
            }
        }

        #endregion

        private float[]? ConvertFrameToFloatArrayInternal(AVFrame* frame)
        {
            try
            {
                var sampleCount = frame->nb_samples * this.OutputChannels;
                var result = new float[sampleCount];
                Marshal.Copy((IntPtr)frame->data[0], result, 0, sampleCount);
                return result;
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error converting frame to float array");
                return null;
            }
        }

        #endregion

        #region Mixing process

        private void StartMixingProcess()
        {
            if (this._isMixing || this._disposed)
            {
                return;
            }

            this._isMixing = true;
            this._shouldStop = false;
            this._hasEmittedFirst = false;
            this._hasEmittedLast = false;
            this._draining = false;
            this._pendingFinalLastFrame = false;
            this.SetState(AudioMixerState.Mixing);

            // Reset pacing/buffering state for a new mixing session.
            // Otherwise a previous session may have left the playback timer running or
            // the buffer marked as prefilled, causing incorrect output pacing and end-frame ordering.
            lock (this._bufferLock)
            {
                this._outputBuffer.Clear();
                this._bufferPreFilled = false;
                this._playbackStarted = false;
                this._lastScheduledOutputTime = DateTime.MinValue;
            }
            this._playbackTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            this._playbackTimer?.Dispose();
            this._playbackTimer = null;

            this._mixingThread = new Thread(this.MixingThreadProc)
            {
                Name = $"FFmpegAudioMixer-{this.GetHashCode()}",
                IsBackground = true
            };
            this._mixingThread.Start();

            this._logger.LogDebug("Started mixing process");
        }

        private void MixingThreadProc()
        {
            try
            {
                while (!this._shouldStop && !this._cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (!this.ProcessMixing())
                    {
                        // If no active streams and last frame has not been emitted, try draining
                        if (this._audioStreams.Count == 0 && !this._hasEmittedLast && this._filterGraph != null)
                        {
                            this.DrainSinkAndEmitLast();
                        }

                        // Check if all streams are completed and we should stop the thread
                        bool shouldStopThread = false;
                        lock (this._streamLock)
                        {
                            if (this._audioStreams.Count == 0 && this._lastFrameEmitted)
                            {
                                shouldStopThread = true;
                            }
                        }

                        if (shouldStopThread)
                        {
                            this._logger.LogDebug("All streams completed, stopping mixing thread to allow restart for next session");
                            break;
                        }

                        // Wait for data or check whether we should stop
                        this._dataAvailableEvent.Wait(TimeSpan.FromMilliseconds(50), this._cancellationTokenSource.Token);
                        this._dataAvailableEvent.Reset();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                this._logger.LogDebug("Mixing thread cancelled");
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error in mixing thread");
            }
            finally
            {
                this._isMixing = false;
                this.SetState(AudioMixerState.Idle);
                this._logger.LogDebug("Mixing thread stopped");
            }
        }

        private bool ProcessMixing()
        {
            lock (this._streamLock)
            {
                if (this._filterGraphDirty)
                {
                    this._filterGraphDirty = false;
                    this.ReconfigureFilterGraph();
                }

                if (this._filterGraph == null)
                {
                    return false;
                }

                bool hasProcessedData = false;

                // Check whether the output buffer is full
                lock (this._bufferLock)
                {
                    if (this._outputBuffer.Count >= Math.Max(1, this._config.MaxOutputBufferFrames))
                    {
                        return false; // Buffer full, pause processing
                    }
                }

                // Get active streams with improved buffer strategy
                var activeStreams = this.GetActiveStreamsWithBufferStrategy();

                string? batchSentenceId = null;

                // Process each active audio stream
                foreach (var (audioType, streamProcessor) in activeStreams)
                {
                    // Read from buffer and push to filter
                    var (processed, sentenceId) = this.ProcessAudioStream(audioType, streamProcessor);
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
                        else
                        {
                            batchSentenceId ??= sentenceId;
                        }
                    }
                }

                bool metaOnlyEmitted = false;
                if (hasProcessedData)
                {
                    lock (this._bufferLock)
                    {
                        this._pendingSentenceIds.Enqueue(batchSentenceId);
                    }
                }
                else if (batchSentenceId != null)
                {
                    // No audio processed, but we have a sentenceId. Emit empty frame.
                    bool isFirst = this._firstFrameAfterStart;
                    if (this._firstFrameAfterStart)
                    {
                        this._firstFrameAfterStart = false;
                    }

                    this.EmitMixedAudio([], isFirst, false, batchSentenceId);
                    metaOnlyEmitted = true;
                }

                // Retrieve mixed audio data from the filter graph
                bool hasActiveTransitions = this._volumeStates.Values.Any(v => v.IsTransitioning);
                if (hasProcessedData || hasActiveTransitions)
                {
                    this.RetrieveMixedAudioData();
                }
                else if (this._audioStreams.Count > 0)
                {
                    // If there are streams but no processed data, we may need to send silent frames to keep continuity
                    bool hasActiveStreams = this._audioStreams.Values.Any(s => !s.IsComplete && !s.IsStopping);
                    if (hasActiveStreams && !metaOnlyEmitted)
                    {
                        // Send a short silent frame to maintain audio continuity
                        var silentFrame = new float[this._frameSampleCount];
                        bool isFirst = this._firstFrameAfterStart;
                        if (this._firstFrameAfterStart)
                        {
                            this._firstFrameAfterStart = false;
                        }

                        this.EmitMixedAudio(silentFrame, isFirst, false, null);
                        this._logger.LogTrace("Emitted silence frame to maintain audio continuity");

                    }
                }

                // Cleanup completed streams
                this.CleanupCompletedStreams();

                return hasProcessedData || hasActiveTransitions || metaOnlyEmitted;
            }
        }

        private List<(AudioType type, AudioStreamProcessor processor)> GetActiveStreamsWithBufferStrategy()
        {
            var activeStreams = new List<(AudioType, AudioStreamProcessor)>();

            foreach (var kvp in this._audioStreams)
            {
                var audioType = kvp.Key;
                var streamProcessor = kvp.Value;

                if (streamProcessor.IsComplete)
                {
                    continue;
                }

                // FIFO check removed
                // if (!_audioFifos.TryGetValue(audioType, out var fifoPtr))
                //    continue;

                // var fifo = (AVAudioFifo*)fifoPtr;
                var availableSamples = streamProcessor.AvailableDataCount; // ffmpeg.av_audio_fifo_size(fifo);
                var requiredSamples = this._frameSampleCount; // / _outputChannels; // AvailableDataCount is total samples (floats)

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
                else if (isNewStream && this._config.EnableSmoothVolumeControl)
                {
                    // Allow partial frame on new streams to reduce startup latency
                    int minRequiredSamples = (int)(requiredSamples * this._config.NewStreamBufferTolerance);
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
            if (!this._sourceFilterCtxs.TryGetValue(audioType, out var sourceCtxPtr))
            {
                return (false, null);
            }

            var sourceCtx = (AVFilterContext*)sourceCtxPtr;

            var availableSamples = streamProcessor.AvailableDataCount;
            var requiredSamples = this._frameSampleCount; // Total samples

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
            else if (isNewStream && this._config.EnableSmoothVolumeControl)
            {
                int minRequiredSamples = (int)(requiredSamples * this._config.NewStreamBufferTolerance);
                canProcess = availableSamples >= minRequiredSamples;
            }
            else if (streamProcessor.IsStopping && availableSamples > 0)
            {
                canProcess = true;
            }

            if (!canProcess)
            {
                return (false, null);
            }

            try
            {
                // Get data from processor
                var audioData = streamProcessor.GetFrameDataWithPartialSupport(this._frameSampleCount, out int samplesRead, out string? sentenceId);

                if (audioData == null || audioData.Length == 0)
                {
                    return (false, sentenceId);
                }

                // Apply volume control
                var volumeState = this.GetOrCreateVolumeState(audioType);
                var targetVolume = this._volumeLevels.GetValueOrDefault(audioType, 0.5f);

                float currentVolume;
                if (!this._config.EnableSmoothVolumeControl)
                {
                    currentVolume = targetVolume;
                }
                else
                {
                    const float Eps = 0.0001f;
                    if (!volumeState.IsTransitioning && Math.Abs(volumeState.TargetVolume - targetVolume) > Eps)
                    {
                        volumeState.StartTransition(targetVolume, this._config.VolumeTransitionDurationMs, this._config.TransitionCurve);
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
                {
                    return (false, null);
                }

                // Set frame parameters
                frame->nb_samples = audioData.Length / this.OutputChannels;

                AVChannelLayout ch;
                ffmpeg.av_channel_layout_from_mask(&ch, this._channelLayout);
                frame->ch_layout = ch;
                frame->format = (int)this._sampleFormat;
                frame->sample_rate = this.OutputSampleRate;
                frame->pts = this._pts;

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
                    this._logger.LogError("Failed to add frame to filter for {AudioType}: {Error}",
                        audioType, ret.FFErrorToText());
                    return (false, null);
                }

                streamProcessor.MarkFrameProcessed();

                this._pts += nbSamples;

                return (true, sentenceId);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error processing audio stream {AudioType}", audioType);
                return (false, null);
            }
        }

        // WriteToAudioFifo removed - we now use AudioStreamProcessor as the buffer

        private void RetrieveMixedAudioData()
        {
            if (this._sinkFilterCtx == null)
            {
                return;
            }

            try
            {
                var frame = ffmpeg.av_frame_alloc();
                if (frame == null)
                {
                    return;
                }

                var ret = ffmpeg.av_buffersink_get_frame(this._sinkFilterCtx, frame);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                {
                    ffmpeg.av_frame_free(&frame);

                    // If there is no available frame but we have active volume transitions, send a silent frame
                    if (this._volumeStates.Values.Any(v => v.IsTransitioning))
                    {
                        foreach (var v in this._volumeStates.Values)
                        {
                            _ = v.UpdateAndGetCurrentVolume();
                        }

                        var silentFrame = new float[this._frameSampleCount];
                        bool isFirst = this._firstFrameAfterStart;
                        if (this._firstFrameAfterStart)
                        {
                            this._firstFrameAfterStart = false;
                        }

                        this.EmitMixedAudio(silentFrame, isFirst, false, null);
                    }
                    return;
                }

                if (ret < 0)
                {
                    this._logger.LogError("Failed to get frame from sink filter: {Error}", ret.FFErrorToText());
                    ffmpeg.av_frame_free(&frame);
                    return;
                }

                var mixedData = this.ConvertFrameToFloatArrayInternal(frame);
                ffmpeg.av_frame_free(&frame);

                if (mixedData != null && mixedData.Length > 0)
                {
                    // Apply audio processing improvements
                    this.ApplyAudioProcessingEnhancements(mixedData);

                    // Update statistics
                    this.UpdateStatistics(mixedData);

                    // Determine whether this is the first and the last frame
                    bool isFirst = this._firstFrameAfterStart;
                    if (this._firstFrameAfterStart)
                    {
                        this._firstFrameAfterStart = false;
                    }

                    string? sentenceId = null;
                    lock (this._bufferLock)
                    {
                        if (this._pendingSentenceIds.Count > 0)
                        {
                            sentenceId = this._pendingSentenceIds.Dequeue();
                        }
                    }

                    // IMPORTANT: do NOT mark last-frame here.
                    // FFmpeg filter output may still have buffered audio and/or the output pacing buffer
                    // may still contain frames. Marking last here can cause the sender to stop early.
                    this.EmitMixedAudio(mixedData, isFirst, false, sentenceId);
                }
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error retrieving mixed audio data");
            }
        }

        private bool IsFullyDrainedForLastFrame()
        {
            // At end-of-session we remove streams and free FIFOs. So the reliable signal is:
            // no active stream processors, no FIFOs left, and no queued output frames.
            if (this._audioStreams.Count != 0)
            {
                return false;
            }
            // if (_audioFifos.Count != 0) return false;
            lock (this._bufferLock)
            {
                return this._outputBuffer.Count == 0;
            }
        }

        private void DrainSinkAndEmitLast()
        {
            if (this._sinkFilterCtx == null || this._hasEmittedLast || this._lastFrameEmitted)
            {
                return;
            }

            try
            {

                while (true)
                {
                    var frame = ffmpeg.av_frame_alloc();
                    if (frame == null)
                    {
                        break;
                    }

                    var ret = ffmpeg.av_buffersink_get_frame(this._sinkFilterCtx, frame);
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

                    var mixedData = this.ConvertFrameToFloatArrayInternal(frame);
                    ffmpeg.av_frame_free(&frame);

                    if (mixedData != null && mixedData.Length > 0)
                    {
                        this.UpdateStatistics(mixedData);

                        bool isFirst = this._firstFrameAfterStart;
                        if (this._firstFrameAfterStart)
                        {
                            this._firstFrameAfterStart = false;
                        }

                        string? sentenceId = null;
                        lock (this._bufferLock)
                        {
                            if (this._pendingSentenceIds.Count > 0)
                            {
                                sentenceId = this._pendingSentenceIds.Dequeue();
                            }
                        }

                        this.EmitMixedAudio(mixedData, isFirst, false, sentenceId);
                    }
                }

                // Send a final silent frame to indicate the end
                // We can't emit last-frame until all already-enqueued audio has played out.
                // Here we only schedule it; PlaybackTimerCallback will emit it once the output queue is empty.
                if (!this._lastFrameEmitted && !this._pendingFinalLastFrame && this.IsFullyDrainedForLastFrame())
                {
                    this._pendingFinalLastFrame = true;
                }
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error draining sink");
            }
        }

        public void ClearAllBuffers()
        {
            lock (this._streamLock)
            {
                // Dispose and clear all streams to ensure mixer stops
                foreach (var streamProcessor in this._audioStreams.Values)
                {
                    streamProcessor.ClearBuffer();
                }
                this._audioStreams.Clear();
                this._sourceClosedStates.Clear();
                this._volumeStates.Clear();

                // clear output buffer
                lock (this._bufferLock)
                {
                    this._outputBuffer.Clear();
                    this._pendingSentenceIds.Clear();
                    this._bufferPreFilled = false;
                }

                // Force filter graph recreation to flush internal buffers
                this._filterGraphDirty = true;

                // Reset state flags
                this._pendingFinalLastFrame = false;
                this._draining = false;
                this._lastFrameEmitted = true; // Allow thread to exit
                this._firstFrameAfterStart = true;

                // Wake up the thread so it can check the stop condition
                this._dataAvailableEvent.Set();

                this._logger.LogDebug("Cleared all audio buffers and streams");
            }
        }

        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            lock (this._filterLock)
            {
                this._shouldStop = true;
                this._cancellationTokenSource.Cancel();

                // Send remaining frames in the output buffer before cleanup
                lock (this._bufferLock)
                {
                    while (this._outputBuffer.Count > 0)
                    {
                        var frame = this._outputBuffer.Dequeue();
                        this.OutputAudioData(frame.Data, frame.IsFirst, frame.IsLast, frame.SentenceId);
                    }
                }

                // Wait for mixing thread to finish
                this._mixingThread?.Join(TimeSpan.FromSeconds(5));

                // Stop the playback timer
                this._playbackTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                this._playbackTimer?.Dispose();

                // Cleanup FFmpeg resources
                this.CleanupFilterGraph();

                // FIFO free removed
                // foreach (var fifoPtr in _audioFifos.Values)
                // {
                //     ffmpeg.av_audio_fifo_free((AVAudioFifo*)fifoPtr);
                // }
                // _audioFifos.Clear();

                // Dispose all stream processors
                foreach (var streamProcessor in this._audioStreams.Values)
                {
                    streamProcessor.Dispose();
                }
                this._audioStreams.Clear();
                this._sourceClosedStates.Clear();

                this._dataAvailableEvent.Dispose();
                this._cancellationTokenSource.Dispose();

                this._disposed = true;
                this.IsInitialized = false;
            }

            this._logger.LogInformation("FFmpeg audio mixer disposed");
        }

        #endregion
    }
}
