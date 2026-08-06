namespace XiaoZhi.Net.Server.Media.Common.Models;

/// <summary>
/// Represents result structure returned by audio decoder while reading audio frame.
/// </summary>
/// <remarks>
/// Initializes <see cref="AudioDecoderResult"/> structure.
/// </remarks>
/// <param name="frame">Decoded audio frame if successfully reads.</param>
/// <param name="succeeded">Whether or not the frame is successfully reads.</param>
/// <param name="eof">Whether or not the decoder reaches end-of-file.</param>
/// <param name="errorMessage">An error message while reading audio frame.</param>
internal readonly struct AudioDecoderResult(AudioFrame? frame, bool succeeded, bool eof, string? errorMessage = default)
{

    /// <summary>
    /// Gets decoded audio frame if successfully reads.
    /// This should returns <c>null</c> if <see cref="IsSucceeded"/> is <c>false</c>.
    /// </summary>
    public AudioFrame? Frame { get; } = frame;

    /// <summary>
    /// Gets whether or not the decoder is successfully reading audio frame.
    /// </summary>
    public bool IsSucceeded { get; } = succeeded;

    /// <summary>
    /// Gets whether or not the decoder reaches end-of-file (cannot be continued) while reading audio frame. 
    /// </summary>
    public bool IsEOF { get; } = eof;

    /// <summary>
    /// Gets error message from the decoder while reading audio frame.
    /// </summary>
    public string? ErrorMessage { get; } = errorMessage;
}
