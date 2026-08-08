using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions;

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
    /// <param name="audioData">The audio data samples.</param>
    /// <param name="isFirst">Indicates whether this is the first audio frame.</param>
    /// <param name="isLast">Indicates whether this is the last audio frame.</param>
    event Action<float[], bool, bool> OnAudioDataAvailable;

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
    /// 0 ~ 1.0f, default is the max one.
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
    Task<bool> CheckFFmpegInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Plays the audio or media associated with this instance.
    /// </summary>
    /// <param name="cancellationToken">用于取消播放的令牌。</param>
    /// <returns>在音频自然播放结束时完成的任务。</returns>
    Task PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends the player for sending buffers to output device.
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the playback.
    /// </summary>
    /// <param name="cancellationToken">取消等待停止完成的令牌。</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Seeks loaded audio to the specified position.
    /// </summary>
    /// <param name="position">Desired seek position.</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
}
