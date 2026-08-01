using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Encoders;

namespace XiaoZhi.Net.Server.Media.Editors
{
    internal class AudioEditor : IAudioEditor
    {
        private readonly IAudioEncoder _audioEncoder;
        private const int DefaultSampleRate = 16000;
        private const int DefaultChannels = 1;
        private const int DefaultBitRate = 128000;

        public AudioEditor(IAudioEncoder audioEncoder)
        {
            this._audioEncoder = audioEncoder;
        }

        public async Task<bool> SaveAudioFileAsync(string filePath, float[] data)
        {
            return await this.SaveAudioFileAsync(filePath, data, DefaultSampleRate, DefaultChannels, DefaultBitRate);
        }

        public async Task<bool> SaveAudioFileAsync(string filePath, float[] data, int sampleRate, int channels, int bitRate)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            }

            if (data is null || data.Length == 0)
            {
                throw new ArgumentException("Audio data cannot be null or empty.", nameof(data));
            }

            if (sampleRate <= 0)
            {
                throw new ArgumentException("Sample rate must be greater than 0.", nameof(sampleRate));
            }

            if (channels <= 0)
            {
                throw new ArgumentException("Channels must be greater than 0.", nameof(channels));
            }

            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return await this._audioEncoder.EncodeAsync(filePath, data, sampleRate, channels, bitRate);
        }

        public async Task<bool> SaveAudioFileAsync(string filePath, byte[] pcmData)
        {
            return await this.SaveAudioFileAsync(filePath, pcmData, DefaultSampleRate, DefaultChannels, DefaultBitRate);
        }

        public async Task<bool> SaveAudioFileAsync(string filePath, byte[] pcmData, int sampleRate, int channels, int bitRate)
        {
            if (pcmData is null || pcmData.Length == 0)
            {
                throw new ArgumentException("PCM data cannot be null or empty.", nameof(pcmData));
            }

            if (pcmData.Length % 2 != 0)
            {
                throw new ArgumentException("PCM data length must be even (16-bit samples).", nameof(pcmData));
            }

            float[] floatData = ConvertS16LEToFloat(pcmData);
            return await this.SaveAudioFileAsync(filePath, floatData, sampleRate, channels, bitRate);
        }

        /// <summary>
        /// Convert 16-bit signed little-endian PCM bytes to float array [-1.0, 1.0]
        /// </summary>
        private static float[] ConvertS16LEToFloat(byte[] pcmBytes)
        {
            int sampleCount = pcmBytes.Length / 2;
            float[] floatData = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
                floatData[i] = sample / 32768f;
            }

            return floatData;
        }
    }
}
