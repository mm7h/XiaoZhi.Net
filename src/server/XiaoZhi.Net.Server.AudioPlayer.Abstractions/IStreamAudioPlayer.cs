namespace XiaoZhi.Net.Server.AudioPlayer.Abstractions
{
    public interface IStreamAudioPlayer : IAudioPlayer
    {
        /// <summary>
        /// Loads an audio stream to the player.
        /// </summary>
        /// <param name="stream">Source audio stream.</param>
        /// <param name="outputSampleRate">Desired output sample rate.</param>
        /// <param name="outputChannels">Desired output channel count.</param>
        /// <returns><c>true</c> if successfully loaded, otherwise, <c>false</c>.</returns>
        Task<bool> LoadAsync(Stream stream, int outputSampleRate, int outputChannels);
    }
}
