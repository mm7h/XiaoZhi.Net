using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.AudioPlayer.Abstractions;

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
    /// Gets a value indicating whether FFmpeg has been successfully initialized.
    /// </summary>
    public bool IsFFmpegInitialized { get; }

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
    /// Checks whether FFmpeg is installed and initialized for use.
    /// </summary>
    /// <remarks>This method verifies the initialization status of FFmpeg. If FFmpeg is not
    /// initialized, it attempts to initialize it. If an error occurs during initialization, the method logs the
    /// error and returns <see langword="false"/>.</remarks>
    /// <returns><see langword="true"/> if FFmpeg is successfully initialized; otherwise, <see langword="false"/>.</returns>
    bool CheckFFmpegInstalled();

    /// <summary>
    /// Plays the audio or media associated with this instance.
    /// </summary>
    /// <param name="waitDone">A value indicating whether the method should block execution until playback is complete.  <see langword="true"/>
    /// to wait for playback to finish; otherwise, <see langword="false"/>.</param>
    void Play(bool waitDone = false);

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
