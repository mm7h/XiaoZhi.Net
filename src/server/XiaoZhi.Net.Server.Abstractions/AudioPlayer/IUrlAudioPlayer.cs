namespace XiaoZhi.Net.Server.Abstractions.AudioPlayer
{
    public interface IUrlAudioPlayer : IAudioPlayer
    {
        /// <summary>
        /// Loads an audio URL to the player.
        /// </summary>
        /// <param name="url">Audio URL or audio file path.</param>
        /// <param name="outputSampleRate">Desired output sample rate.</param>
        /// <param name="outputChannels">Desired output channel count.</param>
        /// <returns><c>true</c> if successfully loaded, otherwise, <c>false</c>.</returns>
        Task<bool> LoadAsync(string url, int outputSampleRate, int outputChannels);
    }
}
