using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Abstractions.AudioPlayer;

/// <summary>
/// An interface for loading and controlling audio playback.
/// <para>Implements: <see cref="IDisposable"/>.</para>
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>
    /// Event that is raised when player state has been changed.
    /// </summary>
    event Action<PlaybackState> StateChanged;

    /// <summary>
    /// Event that is raised when player position has been changed.
    /// </summary>
    event Action<TimeSpan> PositionChanged;

    /// <summary>
    /// Event that is raised when audio data is available.
    /// </summary>
    event Action<byte[]> OnAudioDataAvailable;

    /// <summary>
    /// Gets whether or not an audio source is loaded and ready for playback.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Gets total duration from loaded audio file.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Gets current player position.
    /// </summary>
    TimeSpan Position { get; }

    /// <summary>
    /// Gets current playback state.
    /// </summary>
    PlaybackState State { get; }

    /// <summary>
    /// Gets whether or not the player is currently seeking an audio stream.
    /// </summary>
    bool IsSeeking { get; }

    /// <summary>
    /// Gets or sets audio volume.
    /// </summary>
    float Volume { get; set; }

    /// <summary>
    /// Gets or sets custom sample processor.
    /// </summary>
    ISampleProcessor? CustomSampleProcessor { get; set; }

    /// <summary>
    /// Starts audio playback.
    /// </summary>
    void Play();

    /// <summary>
    /// Suspends the player for sending buffers to output device.
    /// </summary>
    void Pause();

    /// <summary>
    /// Stop the playback.
    /// </summary>
    void Stop();

    /// <summary>
    /// Seeks loaded audio to the specified position.
    /// </summary>
    /// <param name="position">Desired seek position.</param>
    void Seek(TimeSpan position);
}
