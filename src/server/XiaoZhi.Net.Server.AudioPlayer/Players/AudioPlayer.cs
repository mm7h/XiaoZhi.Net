using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using XiaoZhi.Net.Server.AudioPlayer;
using XiaoZhi.Net.Server.AudioPlayer.Common.Dtos;
using XiaoZhi.Net.Server.AudioPlayer.Common.Enums;
using XiaoZhi.Net.Server.AudioPlayer.Decoders;
using XiaoZhi.Net.Server.AudioPlayer.Decoders.FFmpeg;
using XiaoZhi.Net.Server.AudioPlayer.Processors;
using XiaoZhi.Net.Server.AudioPlayer.Utilities.Extensions;

namespace Bufdio.Players;

/// <summary>
/// A class that provides functionalities for loading and controlling audio playback.
/// <para>Implements: <see cref="IAudioPlayer"/></para>
/// </summary>
public class AudioPlayer : IAudioPlayer
{
    private const int MinQueueSize = 8;
    private const int MaxQueueSize = 128;
    private bool _disposed;
    private readonly ILogger<AudioPlayer> _logger;
    private FFmpegDecoderOptions? _decoderOptions;

    /// <summary>
    /// Initializes <see cref="AudioPlayer"/> instance by providing <see cref="FFmpegDecoderOptions"/> instance.
    /// The audio engine will be automatically configured to match the decoder output format.
    /// </summary>
    public AudioPlayer(ILogger<AudioPlayer> logger)
    {
        _logger = logger;
        VolumeProcessor = new VolumeProcessor { Volume = 1 };
        Queue = new ConcurrentQueue<AudioFrame>();
    }

    /// <inheritdoc />
    public event Action<PlaybackState>? StateChanged;

    /// <inheritdoc />
    public event Action<TimeSpan>? PositionChanged;

    public event Action<byte[]>? OnAudioDataAvailable;

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
        set => VolumeProcessor.Volume = value.VerifyVolume();
    }

    /// <inheritdoc />
    public ISampleProcessor CustomSampleProcessor { get; set; }

    /// <summary>
    /// Gets or sets current <see cref="IAudioDecoder"/> instance.
    /// </summary>
    protected IAudioDecoder CurrentDecoder { get; set; }

    /// <summary>
    /// Gets or sets current specified audio URL.
    /// </summary>
    protected string CurrentUrl { get; set; }

    /// <summary>
    /// Gets or sets current specified audio stream.
    /// </summary>
    protected Stream CurrentStream { get; set; }

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
    protected Thread DecoderThread { get; private set; }

    /// <summary>
    /// Gets current audio engine thread.
    /// </summary>
    protected Thread EngineThread { get; private set; }

    /// <summary>
    /// Gets whether or not the decoder thread reach end of file.
    /// </summary>
    protected bool IsEOF { get; private set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when given url is null.</exception>
    public Task<bool> LoadAsync(string url, int outputSampleRate, int outputChannels)
    {
        if (string.IsNullOrEmpty(url))
        {

            return Task.FromResult(false);
        }
        if (State == PlaybackState.Idle)
        {
            // Playback thread is currently running.
            return Task.FromResult(false);
        }
        FFmpegDecoderOptions decoderOptions = new(outputChannels, outputSampleRate);
        _decoderOptions = decoderOptions;

        LoadInternal(() => CreateDecoder(url));

        if (IsLoaded)
        {
            CurrentUrl = url;
            CurrentStream = null;
        }

        return Task.FromResult(IsLoaded);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when given stream is null.</exception>
    public Task<bool> LoadAsync(Stream stream, int outputSampleRate, int outputChannels)
    {
        if (stream is null)
        {

            return Task.FromResult(false);
        }
        if (State == PlaybackState.Idle)
        {
            // Playback thread is currently running.
            return Task.FromResult(false);
        }
        FFmpegDecoderOptions decoderOptions = new(outputChannels, outputSampleRate);
        _decoderOptions = decoderOptions;

        LoadInternal(() => CreateDecoder(stream));

        if (IsLoaded)
        {
            CurrentUrl = null;
            CurrentStream = stream;
        }

        return Task.FromResult(IsLoaded);
    }

    /// <inheritdoc />
    /// <exception cref="BufdioException">Thrown when audio is not loaded.</exception>
    public void Play()
    {
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

        DecoderThread = new Thread(RunDecoder) { Name = "Decoder Thread", IsBackground = true };
        EngineThread = new Thread(RunEngine) { Name = "Engine Thread", IsBackground = true };

        SetAndRaiseStateChanged(PlaybackState.Playing);

        DecoderThread.Start();
        EngineThread.Start();
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (State is PlaybackState.Playing or PlaybackState.Buffering)
        {
            SetAndRaiseStateChanged(PlaybackState.Paused);
        }
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
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

        _logger?.LogDebug("Seeking to: {position}.", position);

        if (!CurrentDecoder.TrySeek(position, out var error))
        {
            _logger?.LogDebug("Unable to seek audio stream: {error}", error);
            IsSeeking = false;
            return;
        }

        IsSeeking = false;
        SetAndRaisePositionChanged(position);

        _logger?.LogDebug("Successfully seeks to {position}.", position);
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (State == PlaybackState.Idle)
        {
            return;
        }

        State = PlaybackState.Idle;
        EnsureThreadsDone();
        StateChanged?.Invoke(State);
    }

    /// <summary>
    /// Creates an <see cref="IAudioDecoder"/> instance.
    /// By default, it will returns a new <see cref="FFmpegDecoder"/> instance.
    /// </summary>
    /// <param name="url">Audio URL or path to be loaded.</param>
    /// <returns>A new <see cref="FFmpegDecoder"/> instance.</returns>
    protected virtual IAudioDecoder CreateDecoder(string url)
    {
        if (_decoderOptions is null)
        {
            throw new ArgumentNullException("Decoder options is not set.");
        }
        return new FFmpegUrlDecoder(url, _decoderOptions);
    }

    /// <summary>
    /// Creates an <see cref="IAudioDecoder"/> instance.
    /// By default, it will returns a new <see cref="FFmpegDecoder"/> instance.
    /// </summary>
    /// <param name="stream">Audio stream to be loaded.</param>
    /// <param name="decoderOptions">A <see cref="FFmpegDecoderOptions"/> instance.</param>
    /// <returns>A new <see cref="FFmpegDecoder"/> instance.</returns>
    protected virtual IAudioDecoder CreateDecoder(Stream stream)
    {
        if (_decoderOptions is null)
        {
            throw new ArgumentNullException("Decoder options is not set.");
        }
        return new FFmpegStreamDecoder(stream, _decoderOptions);
    }

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
    /// Handles audio decoder error, returns <c>true</c> to continue decoder thread, <c>false</c> will
    /// break the thread. By default, this will try to re-initializes <see cref="CurrentDecoder"/>
    /// and seeks to the last position.
    /// </summary>
    /// <param name="result">Failed audio decoder result.</param>
    /// <returns><c>true</c> will continue decoder thread, <c>false</c> will break the thread.</returns>
    protected virtual bool HandleDecoderError(AudioDecoderResult result)
    {
        Queue.Clear();
        _logger?.LogDebug("Failed to decode audio frame, retrying: {resultErrorMessage}", result.ErrorMessage);

        CurrentDecoder?.Dispose();
        CurrentDecoder = null;

        while (CurrentDecoder == null)
        {
            if (State == PlaybackState.Idle)
            {
                IsLoaded = false;
                return false;
            }

            try
            {
                CurrentDecoder = CurrentUrl != null ? CreateDecoder(CurrentUrl) : CreateDecoder(CurrentStream);
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Unable to recreate audio decoder, retrying: {exMessage}", ex.Message);
                Thread.Sleep(1000);
            }
        }

        _logger?.LogDebug("Audio decoder has been recreated, seeking to the last position ({Position}).", Position);
        Seek(Position);

        return true;
    }

    /// <summary>
    /// Run <see cref="VolumeProcessor"/> and <see cref="CustomSampleProcessor"/> to the specified samples.
    /// </summary>
    /// <param name="samples">Audio samples to process to.</param>
    protected virtual void ProcessSampleProcessors(Span<float> samples)
    {
        if (CustomSampleProcessor is { IsEnabled: true })
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

    private void LoadInternal(Func<IAudioDecoder> decoderFactory)
    {
        _logger.LogDebug("Loading audio to the player.");

        CurrentDecoder?.Dispose();
        CurrentDecoder = null;
        IsLoaded = false;

        try
        {
            CurrentDecoder = decoderFactory();
            Duration = CurrentDecoder.StreamInfo.Duration;

            _logger.LogDebug("Audio successfully loaded.");
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            CurrentDecoder = null;
            _logger.LogDebug("Failed to load audio: {exMessage}", ex.Message);
            IsLoaded = false;
        }

        SetAndRaisePositionChanged(TimeSpan.Zero);
    }

    private void RunDecoder()
    {
        _logger.LogDebug("Decoder thread is started.");

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

        _logger.LogDebug("Decoder thread is completed.");
    }

    private void RunEngine()
    {
        _logger.LogDebug("Engine thread is started.");

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

        _logger.LogDebug("Engine thread is completed.");
    }

    private void EnsureThreadsDone()
    {
        EngineThread?.EnsureThreadDone();
        DecoderThread?.EnsureThreadDone();

        EngineThread = null;
        DecoderThread = null;
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
