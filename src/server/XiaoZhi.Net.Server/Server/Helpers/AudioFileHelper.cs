using MP3Sharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace XiaoZhi.Net.Server.Helpers
{
    /// <summary>
    /// 音频文件处理工具类
    /// </summary>
    public static class AudioFileHelper
    {
        /// <summary>
        /// 解码MP3文件为PCM数据
        /// </summary>
        /// <param name="filePath">MP3文件路径</param>
        /// <param name="frameSize">每帧大小</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>包含PCM数据和音频时长(秒)的元组</returns>
        public static (float[] audioData, double duration) DecodeMP3File(string filePath, int frameSize)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"MP3 file not found: {filePath}", filePath);

            using var mp3Stream = new MP3Stream(filePath);

            // 获取音频格式信息
            int sampleRate = mp3Stream.Frequency;
            int channels = mp3Stream.ChannelCount;

            // 创建缓冲区
            List<float> audioData = new List<float>();
            byte[] buffer = new byte[frameSize * 2]; // 16-bit PCM = 2 bytes per sample
            int bytesRead;

            // 读取MP3数据并转换为浮点数
            while ((bytesRead = mp3Stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                // 将16位PCM转换为浮点数
                for (int i = 0; i < bytesRead; i += 2)
                {
                    if (i + 1 < bytesRead)
                    {
                        short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                        float normalizedSample = sample / 32768.0f;
                        audioData.Add(normalizedSample);
                    }
                }
            }

            float[] result = audioData.ToArray();
            // 计算音频时长(秒) = 样本数 / 采样率
            double duration = (double)result.Length / sampleRate;

            return (result, duration);
        }

        /// <summary>
        /// 解码WAV文件为PCM数据
        /// </summary>
        /// <param name="filePath">WAV文件路径</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>包含PCM数据和音频时长(秒)的元组</returns>
        public static (float[] audioData, double duration) DecodeWavFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"WAV file not found: {filePath}", filePath);

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fileStream);

            // 读取WAV头信息
            // RIFF头
            string riffHeader = new string(reader.ReadChars(4));
            if (riffHeader != "RIFF")
                throw new InvalidDataException("Not a valid WAV file: Missing RIFF header");

            // 文件大小
            reader.ReadInt32();

            // WAVE标识
            string waveHeader = new string(reader.ReadChars(4));
            if (waveHeader != "WAVE")
                throw new InvalidDataException("Not a valid WAV file: Missing WAVE identifier");

            // fmt子块
            string fmtHeader = new string(reader.ReadChars(4));
            if (fmtHeader != "fmt ")
                throw new InvalidDataException("Not a valid WAV file: Missing fmt chunk");

            // fmt子块大小
            int fmtSize = reader.ReadInt32();

            // 音频格式(1表示PCM)
            short audioFormat = reader.ReadInt16();
            if (audioFormat != 1)
                throw new InvalidDataException("Only PCM format WAV files are supported");

            // 通道数
            short channels = reader.ReadInt16();

            // 采样率
            int sampleRate = reader.ReadInt32();

            // 字节率
            reader.ReadInt32();

            // 数据块对齐
            reader.ReadInt16();

            // 位深度
            short bitsPerSample = reader.ReadInt16();

            // 跳过fmt子块的额外数据
            if (fmtSize > 16)
                reader.BaseStream.Seek(fmtSize - 16, SeekOrigin.Current);

            // 查找data子块
            string chunkId;
            int chunkSize;

            do
            {
                try
                {
                    chunkId = new string(reader.ReadChars(4));
                    chunkSize = reader.ReadInt32();

                    if (chunkId != "data")
                        reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                }
                catch (EndOfStreamException)
                {
                    throw new InvalidDataException("Data chunk not found in WAV file");
                }
            } while (chunkId != "data");

            // 读取音频数据
            List<float> audioData = new List<float>(chunkSize / (bitsPerSample / 8));
            int bytesPerSample = bitsPerSample / 8;

            byte[] buffer = new byte[chunkSize];
            reader.Read(buffer, 0, chunkSize);

            // 将PCM数据转换为浮点数
            for (int i = 0; i < buffer.Length; i += bytesPerSample)
            {
                if (bitsPerSample == 16)
                {
                    // 16位PCM
                    if (i + 1 < buffer.Length)
                    {
                        short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                        float normalizedSample = sample / 32768.0f;
                        audioData.Add(normalizedSample);
                    }
                }
                else if (bitsPerSample == 8)
                {
                    // 8位PCM
                    byte sample = buffer[i];
                    float normalizedSample = (sample - 128) / 128.0f;
                    audioData.Add(normalizedSample);
                }
                else if (bitsPerSample == 24)
                {
                    // 24位PCM
                    if (i + 2 < buffer.Length)
                    {
                        int sample = (buffer[i] & 0xFF) | ((buffer[i + 1] & 0xFF) << 8) | ((buffer[i + 2] & 0xFF) << 16);
                        // 处理符号位
                        if ((sample & 0x800000) != 0)
                            sample |= ~0xFFFFFF; // 符号位扩展
                        float normalizedSample = sample / 8388608.0f; // 2^23
                        audioData.Add(normalizedSample);
                    }
                }
                else if (bitsPerSample == 32)
                {
                    // 32位PCM
                    if (i + 3 < buffer.Length)
                    {
                        int sample = (buffer[i] & 0xFF) | ((buffer[i + 1] & 0xFF) << 8) |
                                    ((buffer[i + 2] & 0xFF) << 16) | ((buffer[i + 3] & 0xFF) << 24);
                        float normalizedSample = sample / 2147483648.0f; // 2^31
                        audioData.Add(normalizedSample);
                    }
                }
            }

            float[] result = audioData.ToArray();
            // 计算音频时长(秒) = 样本数 / (采样率 * 通道数)
            double duration = (double)result.Length / (sampleRate * channels);

            return (result, duration);
        }

        /// <summary>
        /// 解码音频文件(支持MP3和WAV)
        /// </summary>
        /// <param name="filePath">音频文件路径</param>
        /// <param name="frameSize">每帧大小(仅MP3需要)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>包含PCM数据和音频时长(秒)的元组</returns>
        public static (float[] audioData, double duration) DecodeAudioFile(string filePath, int frameSize)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Audio file not found: {filePath}", filePath);

            string extension = Path.GetExtension(filePath).ToLower();

            switch (extension)
            {
                case ".mp3":
                    return DecodeMP3File(filePath, frameSize);
                case ".wav":
                    return DecodeWavFile(filePath);
                default:
                    throw new NotSupportedException($"Unsupported audio format: {extension}");
            }
        }
    }
}