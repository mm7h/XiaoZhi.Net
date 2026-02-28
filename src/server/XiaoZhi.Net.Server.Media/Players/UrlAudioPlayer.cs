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
internal class UrlAudioPlayer : AudioPlayerBase<string, UrlAudioPlayer>, IUrlAudioPlayer
{
    private FFmpegDecoderOptions? _decoderOptions;

    /// <summary>
    /// Initializes <see cref="UrlAudioPlayer"/> instance by providing <see cref="FFmpegDecoderOptions"/> instance.
    /// The audio engine will be automatically configured to match the decoder output format.
    /// </summary>
    public UrlAudioPlayer(ILogger<UrlAudioPlayer> logger) : base(logger)
    {
        CurrentUrl = string.Empty;
    }

    public override string AudioPlayerName => nameof(UrlAudioPlayer);

    /// <summary>
    /// Gets or sets current specified audio URL.
    /// </summary>
    public string CurrentUrl { get; private set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when given url is null.</exception>
    public Task<bool> LoadAsync(string url, int outputSampleRate, int outputChannels, int frameDuration)
    {
        if (string.IsNullOrEmpty(url))
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

        LoadInternal(() => CreateDecoder(url));

        if (IsLoaded)
        {
            CurrentUrl = url;
        }

        return Task.FromResult(IsLoaded);
    }

    /// <summary>
    /// Creates an <see cref="IAudioDecoder"/> instance.
    /// By default, it will returns a new <see cref="FFmpegDecoder"/> instance.
    /// </summary>
    /// <param name="url">Audio URL or path to be loaded.</param>
    /// <returns>A new <see cref="FFmpegDecoder"/> instance.</returns>
    protected override IAudioDecoder CreateDecoder(string url)
    {
        if (_decoderOptions is null)
        {
            throw new InvalidOperationException("Decoder options is not set.");
        }
        return new FFmpegUrlDecoder(url, _decoderOptions);
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

        while (CurrentDecoder is null)
        {
            if (State == PlaybackState.Idle)
            {
                IsLoaded = false;
                return false;
            }

            try
            {
                CurrentDecoder = CreateDecoder(CurrentUrl);
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
