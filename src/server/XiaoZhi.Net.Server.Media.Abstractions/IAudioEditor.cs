namespace XiaoZhi.Net.Server.Media.Abstractions
{
    public interface IAudioEditor
    {
        /// <summary>
        /// Save float audio data to file with default settings (16kHz, mono, 128kbps)
        /// </summary>
        /// <param name="filePath">Output file path (format determined by extension)</param>
        /// <param name="data">Audio samples normalized to [-1.0, 1.0]</param>
        Task<bool> SaveAudioFileAsync(string filePath, float[] data);

        /// <summary>
        /// Save float audio data to file with specified settings
        /// </summary>
        /// <param name="filePath">Output file path (format determined by extension)</param>
        /// <param name="data">Audio samples normalized to [-1.0, 1.0]</param>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <param name="channels">Number of audio channels</param>
        /// <param name="bitRate">Bit rate for encoding</param>
        Task<bool> SaveAudioFileAsync(string filePath, float[] data, int sampleRate, int channels, int bitRate);

        /// <summary>
        /// Save 16-bit signed little-endian PCM data to file with default settings (16kHz, mono, 128kbps)
        /// </summary>
        /// <param name="filePath">Output file path (format determined by extension)</param>
        /// <param name="pcmData">Raw 16-bit signed little-endian PCM bytes</param>
        Task<bool> SaveAudioFileAsync(string filePath, byte[] pcmData);

        /// <summary>
        /// Save 16-bit signed little-endian PCM data to file with specified settings
        /// </summary>
        /// <param name="filePath">Output file path (format determined by extension)</param>
        /// <param name="pcmData">Raw 16-bit signed little-endian PCM bytes</param>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <param name="channels">Number of audio channels</param>
        /// <param name="bitRate">Bit rate for encoding</param>
        Task<bool> SaveAudioFileAsync(string filePath, byte[] pcmData, int sampleRate, int channels, int bitRate);
    }
}
