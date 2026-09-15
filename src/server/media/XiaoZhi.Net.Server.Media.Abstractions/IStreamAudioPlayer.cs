namespace XiaoZhi.Net.Server.Media.Abstractions
{
    public interface IStreamAudioPlayer : IAudioPlayer
    {
        /// <summary>
        /// Loads an audio stream to the player.
        /// </summary>
        /// <param name="stream">Source audio stream.</param>
        /// <param name="outputSampleRate">Desired output sample rate.</param>
        /// <param name="outputChannels">Desired output channel count.</param>
        /// <param name="frameDuration">Desired output frame duration in milliseconds.</param>
        /// <returns><c>true</c> if successfully loaded, otherwise, <c>false</c>.</returns>
        Task<bool> LoadAsync(Stream stream, int outputSampleRate, int outputChannels, int frameDuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Decodes the loaded audio stream and raises audio-data events without waiting for presentation timestamps.
        /// </summary>
        /// <remarks>
        /// The default implementation preserves compatibility for existing implementations. Implementations that support
        /// fast decoding should override this method.
        /// </remarks>
        Task DecodeAsync(CancellationToken cancellationToken = default)
        {
            return this.PlayAsync(cancellationToken);
        }
    }
}
