using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Common.Dtos;
using XiaoZhi.Net.Server.Media.Decoders;
using XiaoZhi.Net.Server.Media.Decoders.FFmpeg;

namespace XiaoZhi.Net.Server.Media.Players;

/// <summary>
/// A class that provides functionalities for loading and controlling audio playback.
/// <para>Implements: <see cref="IAudioPlayer"/></para>
/// </summary>
internal class StreamAudioPlayer : AudioPlayerBase<Stream, StreamAudioPlayer>, IStreamAudioPlayer
{
    private FFmpegDecoderOptions? _decoderOptions;

    /// <summary>
    /// Initializes <see cref="StreamAudioPlayer"/> instance by providing <see cref="FFmpegDecoderOptions"/> instance.
    /// The audio engine will be automatically configured to match the decoder output format.
    /// </summary>
    public StreamAudioPlayer(ILogger<StreamAudioPlayer> logger) : base(logger)
    {
    }
    public override string AudioPlayerName => nameof(StreamAudioPlayer);

    /// <summary>
    /// Gets or sets current specified audio stream.
    /// </summary>
    protected Stream? CurrentStream { get; set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when given stream is null.</exception>
    public Task<bool> LoadAsync(Stream stream, int outputSampleRate, int outputChannels, int frameDuration)
    {
        if (stream is null)
        {

            return Task.FromResult(false);
        }
        if (State != PlaybackState.Idle)
        {
            // Playback thread is currently running.
            return Task.FromResult(false);
        }
        FFmpegDecoderOptions decoderOptions = new(outputSampleRate, outputChannels, frameDuration);
        _decoderOptions = decoderOptions;

        LoadInternal(() => CreateDecoder(stream));

        if (IsLoaded)
        {
            CurrentStream = stream;
        }

        return Task.FromResult(IsLoaded);
    }


    /// <summary>
    /// Creates an <see cref="IAudioDecoder"/> instance.
    /// By default, it will returns a new <see cref="FFmpegDecoder"/> instance.
    /// </summary>
    /// <param name="stream">Audio stream to be loaded.</param>
    /// <param name="decoderOptions">A <see cref="FFmpegDecoderOptions"/> instance.</param>
    /// <returns>A new <see cref="FFmpegDecoder"/> instance.</returns>
    protected override IAudioDecoder CreateDecoder(Stream stream)
    {
        if (_decoderOptions is null)
        {
            throw new InvalidOperationException("Decoder options is not set.");
        }

        return new FFmpegStreamDecoder(stream, _decoderOptions);
    }

    /// <summary>
    /// Handles audio decoder error, returns <c>true</c> to continue decoder thread, <c>false</c> will
    /// break the thread. By default, this will try to re-initializes <see cref="CurrentDecoder"/>
    /// and seeks to the last position.
    /// </summary>
    /// <param name="result">Failed audio decoder result.</param>
    /// <returns><c>true</c> will continue decoder thread, <c>false</c> will break the thread.</returns>
    protected override bool HandleDecoderError(AudioDecoderResult result)
    {
        Queue.Clear();
        Logger?.LogDebug("Failed to decode audio frame, retrying: {resultErrorMessage}", result.ErrorMessage);

        CurrentDecoder?.Dispose();
        CurrentDecoder = null;

        if (CurrentStream is null)
        {
            IsLoaded = false;
            return false;
        }

        while (CurrentDecoder is null)
        {
            if (State == PlaybackState.Idle)
            {
                IsLoaded = false;
                return false;
            }

            try
            {
                CurrentDecoder = CreateDecoder(CurrentStream);
                break;
            }
            catch (Exception ex)
            {
                Logger?.LogDebug("Unable to recreate audio decoder, retrying: {exMessage}", ex.Message);
                Thread.Sleep(1000);
            }
        }

        Logger?.LogDebug("Audio decoder has been recreated, seeking to the last position ({Position}).", Position);
        Seek(Position);

        return true;
    }
}
