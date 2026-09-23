using System;
using System.IO;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal static class ReferenceWaveReader
    {
        public static (float[] Samples, int SampleRate) Read(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            using BinaryReader reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != 0x46464952)
            {
                throw new InvalidDataException("参考音频必须是 WAV 文件。");
            }
            reader.ReadUInt32();
            if (reader.ReadUInt32() != 0x45564157)
            {
                throw new InvalidDataException("参考音频必须是 WAV 文件。");
            }

            short format = 0;
            short channels = 0;
            int sampleRate = 0;
            short bitsPerSample = 0;
            byte[]? samples = null;
            while (stream.Position + 8 <= stream.Length)
            {
                uint chunkId = reader.ReadUInt32();
                int chunkSize = reader.ReadInt32();
                if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
                {
                    throw new InvalidDataException("参考音频的 WAV 数据块无效。");
                }

                if (chunkId == 0x20746d66)
                {
                    if (chunkSize < 16)
                    {
                        throw new InvalidDataException("参考音频的 WAV 格式数据块无效。");
                    }
                    format = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    bitsPerSample = reader.ReadInt16();
                    stream.Position += chunkSize - 16;
                }
                else if (chunkId == 0x61746164)
                {
                    samples = reader.ReadBytes(chunkSize);
                }
                else
                {
                    stream.Position += chunkSize;
                }

                if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
                {
                    stream.Position++;
                }
            }

            if (format != 1 || channels != 1 || bitsPerSample != 16 || sampleRate <= 0 || samples is null || samples.Length == 0 || (samples.Length & 1) != 0)
            {
                throw new InvalidDataException("参考音频必须是 16 位、单声道 PCM WAV 文件。");
            }

            short[] pcm = new short[samples.Length / sizeof(short)];
            Buffer.BlockCopy(samples, 0, pcm, 0, samples.Length);
            float[] normalized = new float[pcm.Length];
            for (int i = 0; i < pcm.Length; i++)
            {
                normalized[i] = pcm[i] / 32768.0F;
            }
            return (normalized, sampleRate);
        }
    }
}
