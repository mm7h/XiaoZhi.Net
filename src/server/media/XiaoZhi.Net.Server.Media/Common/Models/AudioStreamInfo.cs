namespace XiaoZhi.Net.Server.Media.Common.Models;

/// <summary>
/// Containing audio stream information that is usually retrieved by audio codec.
/// This class cannot be inherited.
/// </summary>
/// <remarks>
/// Initializes <see cref="AudioStreamInfo"/> structure.
/// </remarks>
/// <param name="channels">Number of audio channels.</param>
/// <param name="sampleRate">Audio sample rate.</param>
/// <param name="duration">Audio stream duration.</param>
internal readonly struct AudioStreamInfo(int channels, int sampleRate, TimeSpan duration)
{

    /// <summary>
    /// Gets number of audio channels.
    /// </summary>
    public int Channels { get; } = channels;

    /// <summary>
    /// Gets audio sample rate.
    /// </summary>
    public int SampleRate { get; } = sampleRate;

    /// <summary>
    /// Gets audio stream duration.
    /// </summary>
    public TimeSpan Duration { get; } = duration;
}
