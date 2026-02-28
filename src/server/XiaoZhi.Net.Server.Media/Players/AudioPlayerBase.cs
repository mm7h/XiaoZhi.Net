using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Common.Dtos;
using XiaoZhi.Net.Server.Media.Decoders;
using XiaoZhi.Net.Server.Media.Decoders.FFmpeg;
using XiaoZhi.Net.Server.Media.Exceptions;
using XiaoZhi.Net.Server.Media.Processors;
using XiaoZhi.Net.Server.Media.Utilities;
using XiaoZhi.Net.Server.Media.Utilities.Extensions;

namespace XiaoZhi.Net.Server.Media
{
    internal abstract class AudioPlayerBase<TDecoderType, TLogger> : IAudioPlayer
    {
        private const int MinQueueSize = 8;
        private const int MaxQueueSize = 128;
        private bool _disposed;
        private ManualResetEventSlim? _playbackCompletionEvent;

        public AudioPlayerBase(ILogger<TLogger> logger)
        {
            Logger = logger;
            VolumeProcessor = new VolumeProcessor { Volume = 1.0f };
            Queue = new ConcurrentQueue<AudioFrame>();
        }

        /// <inheritdoc />
        public event Action<PlaybackState>? StateChanged;

        /// <inheritdoc />
        public event Action<TimeSpan>? PositionChanged;

        public event Action<float[], bool, bool>? OnAudioDataAvailable;

        /// <inheritdoc />
        public abstract string AudioPlayerName { get; }

        public bool IsFFmpegInitialized => FFmpegStartup.FFmpegInitialized;

        /// <inheritdoc />
        public bool IsLoaded { get; protected set; }

        /// <inheritdoc />
        public TimeSpan Duration { get; protected set; }

        /// <inheritdoc />
        public TimeSpan Position { get; protected set; }

        /// <inheritdoc />
        public PlaybackState State { get; protected set; }

        /// <inheritdoc />
        public bool IsSeeking { get; private set; }

        /// <inheritdoc />
        public float Volume
        {
            get => VolumeProcessor.Volume;
            set => VolumeProcessor.Volume = VerifyVolume(value);
        }

        /// <inheritdoc />
        public ISampleProcessor? CustomSampleProcessor { get; set; }

        /// <summary>
        /// Gets or sets current <see cref="IAudioDecoder"/> instance.
        /// </summary>
        protected IAudioDecoder? CurrentDecoder { get; set; }

        /// <summary>
        /// Current logger.
        /// </summary>
        protected ILogger<TLogger> Logger { get; }

        /// <summary>
        /// Gets <see cref="VolumeProcessor"/> instance.
        /// </summary>
        protected VolumeProcessor VolumeProcessor { get; }

        /// <summary>
        /// Gets queue object that holds queued audio frames.
        /// </summary>
        protected ConcurrentQueue<AudioFrame> Queue { get; }

        /// <summary>
        /// Gets current audio decoder thread.
        /// </summary>
        protected Thread? DecoderThread { get; private set; }

        /// <summary>
        /// Gets current audio engine thread.
        /// </summary>
        protected Thread? EngineThread { get; private set; }

        /// <summary>
        /// Gets whether or not the decoder thread reach end of file.
        /// </summary>
        protected bool IsEOF { get; private set; }

        /// <summary>
        /// Tracks the playback start time for timing synchronization.
        /// </summary>
        private DateTime _playbackStartTime;

        /// <summary>
        /// Tracks whether this is the first frame being processed.
        /// </summary>
        private bool _firstFrame;

        /// <summary>
        /// Tracks total pause duration to adjust playback timing.
        /// </summary>
        private TimeSpan _totalPauseDuration;

        /// <summary>
        /// Tracks when seeking occurred to reset pause tracking.
        /// </summary>
        private bool _seekOccurred;

        /// <summary>
        /// Checks whether FFmpeg is installed and initialized for use.
        /// </summary>
        /// <remarks>This method verifies the initialization status of FFmpeg. If FFmpeg is not
        /// initialized, it attempts to initialize it. If an error occurs during initialization, the method logs the
        /// error and returns <see langword="false"/>.</remarks>
        /// <returns><see langword="true"/> if FFmpeg is successfully initialized; otherwise, <see langword="false"/>.</returns>
        public bool CheckFFmpegInstalled()
        {
            if (IsFFmpegInitialized)
            {
                return true;
            }
            bool checkResult = FFmpegStartup.CheckFFmpegInstalled(out var message);

            if (checkResult)
            {
                Logger.LogInformation("Initialized the ffmpeg, version: {v}", message); 
                return true;
            }
            else
            {
                Logger.LogError("FFmpeg is not installed or failed to initialize: {message}", message);
                return false;
            }
        }

        /// <inheritdoc />
        public void Play(bool waitDone = false)
        {
            if (!IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke the function \"CheckFFmpegInstalled()\" first.");
            }

            if (!IsLoaded)
            {
                Logger.LogDebug("No loaded audio for playback.");
                return;
            }

            if (State is PlaybackState.Playing or PlaybackState.Buffering)
            {
                Logger.LogDebug("The player is running.");
                return;
            }

            if (State == PlaybackState.Paused)
            {
                SetAndRaiseStateChanged(PlaybackState.Playing);
                return;
            }

            EnsureThreadsDone();

            Seek(Position);
            IsEOF = false;

            // Reset timing tracking for new playback
            _playbackStartTime = DateTime.Now;
            _firstFrame = true;

            // Create completion event if waitDone is requested
            if (waitDone)
            {
                _playbackCompletionEvent?.Dispose();
                _playbackCompletionEvent = new ManualResetEventSlim(false);
            }

            DecoderThread = new Thread(RunDecoder) { Name = $"Decoder_Thread_{AudioPlayerName}", IsBackground = true };
            EngineThread = new Thread(RunEngine) { Name = $"Engine_Thread_{AudioPlayerName}", IsBackground = true };

            SetAndRaiseStateChanged(PlaybackState.Playing);

            DecoderThread.Start();
            EngineThread.Start();

            if (waitDone)
            {
                try
                {
                    Logger.LogDebug("Waiting for playback to complete...");
                    _playbackCompletionEvent?.Wait();
                    Logger.LogDebug("Playback completed.");
                }
                catch (ObjectDisposedException)
                {
                    // Event was disposed, which means playback was stopped
                    Logger.LogDebug("Playback was stopped.");
                }
                finally
                {
                    _playbackCompletionEvent?.Dispose();
                    _playbackCompletionEvent = null;
                }
            }
        }

        /// <inheritdoc />
        public void Pause()
        {
            if (!IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke the function \"CheckFFmpegInstalled()\" first.");
            }
            if (State is PlaybackState.Playing or PlaybackState.Buffering)
            {
                SetAndRaiseStateChanged(PlaybackState.Paused);
            }
        }

        /// <inheritdoc />
        public void Seek(TimeSpan position)
        {
            if (!IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke the function \"CheckFFmpegInstalled()\" first.");
            }
            if (!IsLoaded || IsSeeking || CurrentDecoder == null)
            {
                return;
            }

            IsSeeking = true;
            Queue.Clear();

            // Sleep to produce smooth seek
            if (DecoderThread is { IsAlive: true } || EngineThread is { IsAlive: true })
            {
                Thread.Sleep(100);
            }

            Logger?.LogDebug("Seeking to: {position}.", position);

            if (!CurrentDecoder.TrySeek(position, out var error))
            {
                Logger?.LogDebug("Unable to seek audio stream: {error}", error);
                IsSeeking = false;
                return;
            }

            // Reset timing tracking when seeking
            _playbackStartTime = DateTime.Now - position;
            _firstFrame = true;
            _totalPauseDuration = TimeSpan.Zero;
            _seekOccurred = true;

            IsSeeking = false;
            SetAndRaisePositionChanged(position);

            Logger?.LogDebug("Successfully seeks to {position}.", position);
        }

        /// <inheritdoc />
        public void Stop()
        {
            if (!IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke the function \"CheckFFmpegInstalled()\" first.");
            }
            if (State == PlaybackState.Idle)
            {
                return;
            }

            State = PlaybackState.Idle;

            // Interrupt the decoder if it supports interruption
            if (CurrentDecoder is FFmpegStreamDecoder streamDecoder)
            {
                streamDecoder.Interrupt();
            }

            EnsureThreadsDone();

            // Signal completion event before invoking StateChanged
            _playbackCompletionEvent?.Set();

            StateChanged?.Invoke(State);
        }

        protected abstract IAudioDecoder CreateDecoder(TDecoderType decoderParam);

        /// <summary>
        /// Handles audio decoder error, returns <c>true</c> to continue decoder thread, <c>false</c> will
        /// break the thread. By default, this will try to re-initializes <see cref="CurrentDecoder"/>
        /// and seeks to the last position.
        /// </summary>
        /// <param name="result">Failed audio decoder result.</param>
        /// <returns><c>true</c> will continue decoder thread, <c>false</c> will break the thread.</returns>
        protected abstract bool HandleDecoderError(AudioDecoderResult result);

        /// <summary>
        /// Sets <see cref="State"/> value and raise <see cref="StateChanged"/> if value is changed.
        /// </summary>
        /// <param name="state">Playback state.</param>
        protected virtual void SetAndRaiseStateChanged(PlaybackState state)
        {
            var raise = State != state;
            State = state;

            if (raise && StateChanged != null)
            {
                StateChanged.Invoke(State);
            }

            // Signal completion when state changes to Idle
            if (state == PlaybackState.Idle && _playbackCompletionEvent != null)
            {
                _playbackCompletionEvent.Set();
            }
        }

        /// <summary>
        /// Sets <see cref="Position"/> value and raise <see cref="PositionChanged"/> if value is changed.
        /// </summary>
        /// <param name="position">Playback position.</param>
        protected virtual void SetAndRaisePositionChanged(TimeSpan position)
        {
            var raise = position != Position;
            Position = position;

            if (raise && PositionChanged != null)
            {
                PositionChanged.Invoke(Position);
            }
        }

        /// <summary>
        /// Run <see cref="VolumeProcessor"/> and <see cref="CustomSampleProcessor"/> to the specified samples.
        /// </summary>
        /// <param name="samples">Audio samples to process to.</param>
        protected virtual void ProcessSampleProcessors(Span<float> samples)
        {
            if (CustomSampleProcessor is not null && CustomSampleProcessor is { IsEnabled: true })
            {
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = CustomSampleProcessor.Process(samples[i]);
                }
            }

            if (VolumeProcessor.Volume != 1.0f)
            {
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = VolumeProcessor.Process(samples[i]);
                }
            }
        }

        protected void LoadInternal(Func<IAudioDecoder> decoderFactory)
        {
            Logger.LogDebug("Loading audio to the player.");

            CurrentDecoder?.Dispose();
            CurrentDecoder = null;
            IsLoaded = false;

            try
            {
                CurrentDecoder = decoderFactory();
                Duration = CurrentDecoder.StreamInfo.Duration;

                Logger.LogDebug("Audio successfully loaded.");
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                CurrentDecoder = null;
                Logger.LogDebug("Failed to load audio: {exMessage}", ex.Message);
                IsLoaded = false;
            }

            SetAndRaisePositionChanged(TimeSpan.Zero);
        }

        private void RunDecoder()
        {
            Logger.LogDebug("Decoder thread is started.");
            while (State != PlaybackState.Idle)
            {
                while (IsSeeking)
                {
                    if (State == PlaybackState.Idle)
                    {
                        break;
                    }

                    Queue.Clear();
                    Thread.Sleep(10);
                }
                if (State == PlaybackState.Idle)
                {
                    break;
                }
                if (CurrentDecoder is null)
                {
                    break;
                }
                if (EngineThread is null)
                {
                    break;
                }
                var result = CurrentDecoder.DecodeNextFrame();

                if (result.IsEOF)
                {
                    IsEOF = true;
                    EngineThread.EnsureThreadDone(() => IsSeeking);

                    if (IsSeeking)
                    {
                        IsEOF = false;
                        Queue.Clear();

                        continue;
                    }

                    break;
                }

                if (!result.IsSucceeded)
                {
                    if (HandleDecoderError(result))
                    {
                        continue;
                    }

                    IsEOF = true; // ends the engine thread
                    break;
                }

                while (Queue.Count >= MaxQueueSize)
                {
                    if (State == PlaybackState.Idle)
                    {
                        break;
                    }

                    Thread.Sleep(100);
                }
                if (State == PlaybackState.Idle)
                {
                    break;
                }
                if (result.Frame is not null)
                {
                    Queue.Enqueue(result.Frame);
                }
            }
            Logger.LogDebug("Decoder thread is completed.");
        }

        private void RunEngine()
        {
            Logger.LogDebug("Engine thread is started.");

            double lastPresentationTime = 0;
            DateTime lastFrameTime = DateTime.Now;
            DateTime pauseStartTime = DateTime.MinValue;
            TimeSpan totalPauseDuration = _totalPauseDuration;
            bool isFirstAudioFrame = true; // mark if this is the first audio frame
            float[]? lastProcessedSamples = null; // save the last processed audio samples
            bool lastEventSent = false; // mark if the last frame event has been sent

            while (State != PlaybackState.Idle)
            {
                if (State == PlaybackState.Paused || IsSeeking)
                {
                    // record the pause start time for calculating total pause duration
                    if (State == PlaybackState.Paused && pauseStartTime == DateTime.MinValue)
                    {
                        pauseStartTime = DateTime.Now;
                    }

                    // when paused, if there are last processed samples and the last event has not been sent, send the event with isLast=true
                    if (State == PlaybackState.Paused && lastProcessedSamples != null && !lastEventSent)
                    {
                        this.OnAudioDataAvailable?.Invoke(lastProcessedSamples, false, true);
                        lastEventSent = true; // mark as sent
                    }

                    Thread.Sleep(10);
                    continue;
                }

                if (_seekOccurred)
                {
                    totalPauseDuration = TimeSpan.Zero;
                    pauseStartTime = DateTime.MinValue;
                    _seekOccurred = false;
                    isFirstAudioFrame = true; // reset to first frame on seek
                    lastProcessedSamples = null; // clear last samples on seek
                    lastEventSent = false; // reset the last event sent flag
                }

                // calculate total pause duration when resuming from pause
                if (pauseStartTime != DateTime.MinValue)
                {
                    var pauseDuration = DateTime.Now - pauseStartTime;
                    totalPauseDuration = totalPauseDuration.Add(pauseDuration);
                    _totalPauseDuration = totalPauseDuration;
                    pauseStartTime = DateTime.MinValue;
                    lastEventSent = false; // reset the flag when resuming playback
                }

                if (Queue.Count < MinQueueSize && !IsEOF)
                {
                    SetAndRaiseStateChanged(PlaybackState.Buffering);
                    Thread.Sleep(10);
                    continue;
                }

                if (!Queue.TryDequeue(out var frame))
                {
                    if (IsEOF)
                    {
                        // send the last processed samples if available and not sent yet
                        if (lastProcessedSamples != null && !lastEventSent)
                        {
                            this.OnAudioDataAvailable?.Invoke(lastProcessedSamples, false, true);
                            lastEventSent = true; // set the flag as sent
                        }
                        break;
                    }

                    Thread.Sleep(10);
                    continue;
                }

                var samples = MemoryMarshal.Cast<byte, float>(frame.Data);
                ProcessSampleProcessors(samples);

                SetAndRaiseStateChanged(PlaybackState.Playing);

                // check if this is the last audio frame: queue is empty and reached EOF
                bool isLastAudioFrame = Queue.IsEmpty && IsEOF;
                
                var samplesArray = samples.ToArray();
                lastProcessedSamples = samplesArray; // save the current processed samples

                this.OnAudioDataAvailable?.Invoke(samplesArray, isFirstAudioFrame, isLastAudioFrame);

                // make sure to send isLast=true only once
                if (isLastAudioFrame)
                {
                    lastEventSent = true;
                }

                // process the first frame flag
                if (isFirstAudioFrame)
                {
                    isFirstAudioFrame = false;
                }

                var framePresentationTime = frame.PresentationTime;

                // If this is the first frame, initialize timing
                if (_firstFrame)
                {
                    _playbackStartTime = DateTime.Now - TimeSpan.FromMilliseconds(framePresentationTime);
                    _firstFrame = false;
                }

                // Calculate when this frame should be played
                var targetPlayTime = _playbackStartTime.AddMilliseconds(framePresentationTime).Add(totalPauseDuration);
                var currentTime = DateTime.Now;
                var timeToWait = targetPlayTime - currentTime;

                // If we're ahead of schedule, wait
                if (timeToWait.TotalMilliseconds > 0)
                {
                    var waitMs = Math.Min((int)timeToWait.TotalMilliseconds, 100);
                    if (waitMs > 0)
                    {
                        Thread.Sleep(waitMs);
                    }
                }

                // Update timing tracking
                lastFrameTime = DateTime.Now;
                lastPresentationTime = framePresentationTime;

                // Update the position to reflect the actual playback timing
                SetAndRaisePositionChanged(TimeSpan.FromMilliseconds(framePresentationTime));
            }

            // once the engine thread ends, if there are last processed samples and the last event has not been sent, send the event with isLast=true
            if (lastProcessedSamples != null && !lastEventSent)
            {
                this.OnAudioDataAvailable?.Invoke(lastProcessedSamples, false, true);
                lastEventSent = true;
            }

            SetAndRaisePositionChanged(TimeSpan.Zero);

            Task.Run(() => SetAndRaiseStateChanged(PlaybackState.Idle));

            Logger.LogDebug("Engine thread is completed.");
        }

        private void EnsureThreadsDone()
        {
            EngineThread?.EnsureThreadDone();
            DecoderThread?.EnsureThreadDone();

            EngineThread = null;
            DecoderThread = null;
        }

        private float VerifyVolume(float volume)
        {
            return volume switch
            {
                > 1.0f => 1.0f,
                < 0.0f => 0.0f,
                _ => volume
            };
        }

        /// <inheritdoc />
        public virtual void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            State = PlaybackState.Idle;

            // Interrupt the decoder if it supports interruption
            if (CurrentDecoder is FFmpegStreamDecoder streamDecoder)
            {
                streamDecoder.Interrupt();
            }

            EnsureThreadsDone();

            // Dispose completion event
            _playbackCompletionEvent?.Dispose();
            _playbackCompletionEvent = null;

            CurrentDecoder?.Dispose();
            Queue.Clear();

            GC.SuppressFinalize(this);

            _disposed = true;
        }
    }
}
