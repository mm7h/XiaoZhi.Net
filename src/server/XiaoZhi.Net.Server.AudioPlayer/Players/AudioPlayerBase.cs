using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.AudioPlayer.Common.Dtos;
using XiaoZhi.Net.Server.AudioPlayer.Decoders;
using XiaoZhi.Net.Server.AudioPlayer.Exceptions;
using XiaoZhi.Net.Server.AudioPlayer.Processors;
using XiaoZhi.Net.Server.AudioPlayer.Utilities.Extensions;

namespace XiaoZhi.Net.Server.AudioPlayer
{
    internal abstract class AudioPlayerBase
    {
        internal static string FFmpegRootPath = "./ffmpeg/";

        /// <summary>
        /// Gets a value indicating whether FFmpeg has been successfully initialized.
        /// </summary>
        public bool IsFFmpegInitialized { get; protected set; }
    }

    internal abstract class AudioPlayerBase<TDecoderType, TLogger> : AudioPlayerBase, IAudioPlayer
    {
        private const int MinQueueSize = 8;
        private const int MaxQueueSize = 128;
        private bool _disposed;

        public AudioPlayerBase(ILogger<TLogger> logger)
        {
            Logger = logger;
            VolumeProcessor = new VolumeProcessor { Volume = 1 };
            Queue = new ConcurrentQueue<AudioFrame>();
        }

        /// <inheritdoc />
        public event Action<PlaybackState>? StateChanged;

        /// <inheritdoc />
        public event Action<TimeSpan>? PositionChanged;

        public event Action<byte[]>? OnAudioDataAvailable;

        /// <inheritdoc />
        public abstract string AudioPlayerName { get; }

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
        /// Checks whether FFmpeg is installed and initialized for use.
        /// </summary>
        /// <remarks>This method verifies the initialization status of FFmpeg. If FFmpeg is not
        /// initialized, it attempts to initialize it. If an error occurs during initialization, the method logs the
        /// error and returns <see langword="false"/>.</remarks>
        /// <returns><see langword="true"/> if FFmpeg is successfully initialized; otherwise, <see langword="false"/>.</returns>
        public bool CheckFFmpegInstalled()
        {
            try
            {
                if (IsFFmpegInitialized)
                {
                    return true;
                }
                Logger.LogInformation("Initialized the ffmpeg, version: {v}", ffmpeg.av_version_info());
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
                IsFFmpegInitialized = true;
                return true;
            }
            catch
            {
                IsFFmpegInitialized = false;
                return false;
            }
        }

        /// <inheritdoc />
        public void Play()
        {
            if (!IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke the function \"CheckFFmpegInstalled()\" first.");
            }

            if (IsLoaded)
            {
                // "No loaded audio for playback."
                return;
            }

            if (State is PlaybackState.Playing or PlaybackState.Buffering)
            {
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

            DecoderThread = new Thread(RunDecoder) { Name = $"Decoder_Thread_{AudioPlayerName}", IsBackground = true };
            EngineThread = new Thread(RunEngine) { Name = $"Engine_Thread_{AudioPlayerName}", IsBackground = true };

            SetAndRaiseStateChanged(PlaybackState.Playing);

            DecoderThread.Start();
            EngineThread.Start();
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
            EnsureThreadsDone();
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

            while (State != PlaybackState.Idle)
            {
                if (State == PlaybackState.Paused || IsSeeking)
                {
                    Thread.Sleep(10);
                    continue;
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
                        break;
                    }

                    Thread.Sleep(10);
                    continue;
                }

                var samples = MemoryMarshal.Cast<byte, float>(frame.Data);
                ProcessSampleProcessors(samples);

                SetAndRaiseStateChanged(PlaybackState.Playing);
                this.OnAudioDataAvailable?.Invoke(frame.Data);

                SetAndRaisePositionChanged(TimeSpan.FromMilliseconds(frame.PresentationTime));
            }

            // Don't calls Seek(), the Play() method will do the job! The Seek() method will sets IsSeeking to true.
            // This can be an endless cycle since the decoder thread will spins and wait the engine thread
            // to complete, and break the spin when IsSeeking value is true.
            SetAndRaisePositionChanged(TimeSpan.Zero);

            // Just fire and forget, and it should be non-blocking event.
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
            EnsureThreadsDone();

            CurrentDecoder?.Dispose();
            Queue.Clear();

            GC.SuppressFinalize(this);

            _disposed = true;
        }
    }
}
