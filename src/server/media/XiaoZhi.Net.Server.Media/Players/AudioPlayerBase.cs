using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Common.Models;
using XiaoZhi.Net.Server.Media.Decoders;
using XiaoZhi.Net.Server.Media.Decoders.FFmpeg;
using XiaoZhi.Net.Server.Media.Exceptions;
using XiaoZhi.Net.Server.Media.Processors;
using XiaoZhi.Net.Server.Media.Utilities;
using XiaoZhi.Net.Server.Media.Utilities.Extensions;

namespace XiaoZhi.Net.Server.Media.Players
{
    internal abstract class AudioPlayerBase<TDecoderType, TLogger> : IAudioPlayer
    {
        private const int MinQueueSize = 8;
        private const int MaxQueueSize = 128;
        private bool _disposed;
        private ManualResetEventSlim? _playbackCompletionEvent;

        public AudioPlayerBase(ILogger<TLogger> logger)
        {
            this.Logger = logger;
            this.VolumeProcessor = new VolumeProcessor { Volume = 1.0f };
            this.Queue = new ConcurrentQueue<AudioFrame>();
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
            get => this.VolumeProcessor.Volume;
            set => this.VolumeProcessor.Volume = this.VerifyVolume(value);
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
            if (this.IsFFmpegInitialized)
            {
                return true;
            }
            bool checkResult = FFmpegStartup.CheckFFmpegInstalled(out var message);

            if (checkResult)
            {
                this.Logger.LogInformation("Initialized the ffmpeg, version: {v}", message);
                return true;
            }
            else
            {
                this.Logger.LogError("FFmpeg is not installed or failed to initialize: {message}", message);
                return false;
            }
        }

        /// <inheritdoc />
        public void Play(bool waitDone = false)
        {
            if (!this.IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke the function \"CheckFFmpegInstalled()\" first.");
            }

            if (!this.IsLoaded)
            {
                this.Logger.LogDebug("No loaded audio for playback.");
                return;
            }

            if (this.State is PlaybackState.Playing or PlaybackState.Buffering)
            {
                this.Logger.LogDebug("The player is running.");
                return;
            }

            if (this.State == PlaybackState.Paused)
            {
                this.SetAndRaiseStateChanged(PlaybackState.Playing);
                return;
            }

            this.EnsureThreadsDone();

            this.Seek(this.Position);
            this.IsEOF = false;

            // Reset timing tracking for new playback
            this._playbackStartTime = DateTime.Now;
            this._firstFrame = true;

            // Create completion event if waitDone is requested
            if (waitDone)
            {
                this._playbackCompletionEvent?.Dispose();
                this._playbackCompletionEvent = new ManualResetEventSlim(false);
            }

            this.DecoderThread = new Thread(this.RunDecoder) { Name = $"Decoder_Thread_{this.AudioPlayerName}", IsBackground = true };
            this.EngineThread = new Thread(this.RunEngine) { Name = $"Engine_Thread_{this.AudioPlayerName}", IsBackground = true };

            this.SetAndRaiseStateChanged(PlaybackState.Playing);

            this.DecoderThread.Start();
            this.EngineThread.Start();

            if (waitDone)
            {
                try
                {
                    this.Logger.LogDebug("Waiting for playback to complete...");
                    this._playbackCompletionEvent?.Wait();
                    this.Logger.LogDebug("Playback completed.");
                }
                catch (ObjectDisposedException)
                {
                    // Event was disposed, which means playback was stopped
                    this.Logger.LogDebug("Playback was stopped.");
                }
                finally
                {
                    this._playbackCompletionEvent?.Dispose();
                    this._playbackCompletionEvent = null;
                }
            }
        }

        /// <inheritdoc />
        public void Pause()
        {
            if (!this.IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke the function \"CheckFFmpegInstalled()\" first.");
            }
            if (this.State is PlaybackState.Playing or PlaybackState.Buffering)
            {
                this.SetAndRaiseStateChanged(PlaybackState.Paused);
            }
        }

        /// <inheritdoc />
        public void Seek(TimeSpan position)
        {
            if (!this.IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke the function \"CheckFFmpegInstalled()\" first.");
            }
            if (!this.IsLoaded || this.IsSeeking || this.CurrentDecoder == null)
            {
                return;
            }

            this.IsSeeking = true;
            this.Queue.Clear();

            // Sleep to produce smooth seek
            if (this.DecoderThread is { IsAlive: true } || this.EngineThread is { IsAlive: true })
            {
                Thread.Sleep(100);
            }

            this.Logger?.LogDebug("Seeking to: {position}.", position);

            if (!this.CurrentDecoder.TrySeek(position, out var error))
            {
                this.Logger?.LogDebug("Unable to seek audio stream: {error}", error);
                this.IsSeeking = false;
                return;
            }

            // Reset timing tracking when seeking
            this._playbackStartTime = DateTime.Now - position;
            this._firstFrame = true;
            this._totalPauseDuration = TimeSpan.Zero;
            this._seekOccurred = true;

            this.IsSeeking = false;
            this.SetAndRaisePositionChanged(position);

            this.Logger?.LogDebug("Successfully seeks to {position}.", position);
        }

        /// <inheritdoc />
        public void Stop()
        {
            if (!this.IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke the function \"CheckFFmpegInstalled()\" first.");
            }
            if (this.State == PlaybackState.Idle)
            {
                return;
            }

            this.State = PlaybackState.Idle;

            // Interrupt the decoder if it supports interruption
            if (this.CurrentDecoder is FFmpegStreamDecoder streamDecoder)
            {
                streamDecoder.Interrupt();
            }

            this.EnsureThreadsDone();

            // Signal completion event before invoking StateChanged
            this._playbackCompletionEvent?.Set();

            StateChanged?.Invoke(this.State);
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
            var raise = this.State != state;
            this.State = state;

            if (raise && StateChanged != null)
            {
                StateChanged.Invoke(this.State);
            }

            // Signal completion when state changes to Idle
            if (state == PlaybackState.Idle && this._playbackCompletionEvent != null)
            {
                this._playbackCompletionEvent.Set();
            }
        }

        /// <summary>
        /// Sets <see cref="Position"/> value and raise <see cref="PositionChanged"/> if value is changed.
        /// </summary>
        /// <param name="position">Playback position.</param>
        protected virtual void SetAndRaisePositionChanged(TimeSpan position)
        {
            var raise = position != this.Position;
            this.Position = position;

            if (raise && PositionChanged != null)
            {
                PositionChanged.Invoke(this.Position);
            }
        }

        /// <summary>
        /// Run <see cref="VolumeProcessor"/> and <see cref="CustomSampleProcessor"/> to the specified samples.
        /// </summary>
        /// <param name="samples">Audio samples to process to.</param>
        protected virtual void ProcessSampleProcessors(Span<float> samples)
        {
            if (this.CustomSampleProcessor is not null && this.CustomSampleProcessor is { IsEnabled: true })
            {
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = this.CustomSampleProcessor.Process(samples[i]);
                }
            }

            if (this.VolumeProcessor.Volume != 1.0f)
            {
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = this.VolumeProcessor.Process(samples[i]);
                }
            }
        }

        protected void LoadInternal(Func<IAudioDecoder> decoderFactory)
        {
            this.Logger.LogDebug("Loading audio to the player.");

            this.CurrentDecoder?.Dispose();
            this.CurrentDecoder = null;
            this.IsLoaded = false;

            try
            {
                this.CurrentDecoder = decoderFactory();
                this.Duration = this.CurrentDecoder.StreamInfo.Duration;

                this.Logger.LogDebug("Audio successfully loaded.");
                this.IsLoaded = true;
            }
            catch (Exception ex)
            {
                this.CurrentDecoder = null;
                this.Logger.LogDebug("Failed to load audio: {exMessage}", ex.Message);
                this.IsLoaded = false;
            }

            this.SetAndRaisePositionChanged(TimeSpan.Zero);
        }

        private void RunDecoder()
        {
            this.Logger.LogDebug("Decoder thread is started.");
            while (this.State != PlaybackState.Idle)
            {
                while (this.IsSeeking)
                {
                    if (this.State == PlaybackState.Idle)
                    {
                        break;
                    }

                    this.Queue.Clear();
                    Thread.Sleep(10);
                }
                if (this.State == PlaybackState.Idle)
                {
                    break;
                }
                if (this.CurrentDecoder is null)
                {
                    break;
                }
                if (this.EngineThread is null)
                {
                    break;
                }
                var result = this.CurrentDecoder.DecodeNextFrame();

                if (result.IsEOF)
                {
                    this.IsEOF = true;
                    this.EngineThread.EnsureThreadDone(() => this.IsSeeking);

                    if (this.IsSeeking)
                    {
                        this.IsEOF = false;
                        this.Queue.Clear();

                        continue;
                    }

                    break;
                }

                if (!result.IsSucceeded)
                {
                    if (this.HandleDecoderError(result))
                    {
                        continue;
                    }

                    this.IsEOF = true; // ends the engine thread
                    break;
                }

                while (this.Queue.Count >= MaxQueueSize)
                {
                    if (this.State == PlaybackState.Idle)
                    {
                        break;
                    }

                    Thread.Sleep(100);
                }
                if (this.State == PlaybackState.Idle)
                {
                    break;
                }
                if (result.Frame is not null)
                {
                    this.Queue.Enqueue(result.Frame);
                }
            }
            this.Logger.LogDebug("Decoder thread is completed.");
        }

        private void RunEngine()
        {
            this.Logger.LogDebug("Engine thread is started.");

            double lastPresentationTime = 0;
            DateTime lastFrameTime = DateTime.Now;
            DateTime pauseStartTime = DateTime.MinValue;
            TimeSpan totalPauseDuration = this._totalPauseDuration;
            bool isFirstAudioFrame = true; // mark if this is the first audio frame
            float[]? lastProcessedSamples = null; // save the last processed audio samples
            bool lastEventSent = false; // mark if the last frame event has been sent

            while (this.State != PlaybackState.Idle)
            {
                if (this.State == PlaybackState.Paused || this.IsSeeking)
                {
                    // record the pause start time for calculating total pause duration
                    if (this.State == PlaybackState.Paused && pauseStartTime == DateTime.MinValue)
                    {
                        pauseStartTime = DateTime.Now;
                    }

                    // when paused, if there are last processed samples and the last event has not been sent, send the event with isLast=true
                    if (this.State == PlaybackState.Paused && lastProcessedSamples != null && !lastEventSent)
                    {
                        this.OnAudioDataAvailable?.Invoke(lastProcessedSamples, false, true);
                        lastEventSent = true; // mark as sent
                    }

                    Thread.Sleep(10);
                    continue;
                }

                if (this._seekOccurred)
                {
                    totalPauseDuration = TimeSpan.Zero;
                    pauseStartTime = DateTime.MinValue;
                    this._seekOccurred = false;
                    isFirstAudioFrame = true; // reset to first frame on seek
                    lastProcessedSamples = null; // clear last samples on seek
                    lastEventSent = false; // reset the last event sent flag
                }

                // calculate total pause duration when resuming from pause
                if (pauseStartTime != DateTime.MinValue)
                {
                    var pauseDuration = DateTime.Now - pauseStartTime;
                    totalPauseDuration = totalPauseDuration.Add(pauseDuration);
                    this._totalPauseDuration = totalPauseDuration;
                    pauseStartTime = DateTime.MinValue;
                    lastEventSent = false; // reset the flag when resuming playback
                }

                if (this.Queue.Count < MinQueueSize && !this.IsEOF)
                {
                    this.SetAndRaiseStateChanged(PlaybackState.Buffering);
                    Thread.Sleep(10);
                    continue;
                }

                if (!this.Queue.TryDequeue(out var frame))
                {
                    if (this.IsEOF)
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
                this.ProcessSampleProcessors(samples);

                this.SetAndRaiseStateChanged(PlaybackState.Playing);

                // check if this is the last audio frame: queue is empty and reached EOF
                bool isLastAudioFrame = this.Queue.IsEmpty && this.IsEOF;

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
                if (this._firstFrame)
                {
                    this._playbackStartTime = DateTime.Now - TimeSpan.FromMilliseconds(framePresentationTime);
                    this._firstFrame = false;
                }

                // Calculate when this frame should be played
                var targetPlayTime = this._playbackStartTime.AddMilliseconds(framePresentationTime).Add(totalPauseDuration);
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
                this.SetAndRaisePositionChanged(TimeSpan.FromMilliseconds(framePresentationTime));
            }

            // once the engine thread ends, if there are last processed samples and the last event has not been sent, send the event with isLast=true
            if (lastProcessedSamples != null && !lastEventSent)
            {
                this.OnAudioDataAvailable?.Invoke(lastProcessedSamples, false, true);
                lastEventSent = true;
            }

            this.SetAndRaisePositionChanged(TimeSpan.Zero);

            Task.Run(() => this.SetAndRaiseStateChanged(PlaybackState.Idle));

            this.Logger.LogDebug("Engine thread is completed.");
        }

        private void EnsureThreadsDone()
        {
            this.EngineThread?.EnsureThreadDone();
            this.DecoderThread?.EnsureThreadDone();

            this.EngineThread = null;
            this.DecoderThread = null;
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
            if (this._disposed)
            {
                return;
            }

            this.State = PlaybackState.Idle;

            // Interrupt the decoder if it supports interruption
            if (this.CurrentDecoder is FFmpegStreamDecoder streamDecoder)
            {
                streamDecoder.Interrupt();
            }

            this.EnsureThreadsDone();

            // Dispose completion event
            this._playbackCompletionEvent?.Dispose();
            this._playbackCompletionEvent = null;

            this.CurrentDecoder?.Dispose();
            this.Queue.Clear();

            GC.SuppressFinalize(this);

            this._disposed = true;
        }
    }
}
