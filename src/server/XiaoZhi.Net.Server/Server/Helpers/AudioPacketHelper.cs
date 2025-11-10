using SherpaOnnx;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Helpers
{
    internal static class AudioPacketHelper
    {

        public static bool GetFrames(this CircularBuffer opusPacketFrame, int size, out float[] data)
        {
            try
            {
                if (opusPacketFrame.Size == 0)
                {
                    data = Array.Empty<float>();
                    return false;
                }

                // 始终尝试获取完整的size大小帧，如果不足则返回实际可用数据
                int framesToGet = Math.Min(opusPacketFrame.Size, size);
                data = opusPacketFrame.Get(opusPacketFrame.Head, framesToGet);

                // 如果获取的帧数小于请求的size，需要进行填充以保持帧大小一致
                if (framesToGet < size)
                {
                    float[] paddedData = new float[size];
                    Array.Copy(data, paddedData, framesToGet);
                    // 剩余部分用0填充
                    for (int i = framesToGet; i < size; i++)
                    {
                        paddedData[i] = 0.0f;
                    }
                    data = paddedData;
                }

                opusPacketFrame.Pop(framesToGet);
                return true;
            }
            catch
            {
                data = Array.Empty<float>();
                return false;
            }
        }

        public static bool GetFrames(this CircularBuffer opusPacketFrame, int size, float[] destination)
        {
            if (destination is null)
                throw new ArgumentNullException(nameof(destination));
            if (destination.Length < size)
                throw new ArgumentException("Destination array is smaller than the requested size.", nameof(destination));
            if (opusPacketFrame.Size == 0)
                return false;
            int framesToGet = Math.Min(opusPacketFrame.Size, size);
            float[] src = opusPacketFrame.Get(opusPacketFrame.Head, framesToGet);
            Array.Copy(src, 0, destination, 0, framesToGet);
            if (framesToGet < size)
            {
                Array.Clear(destination, framesToGet, size - framesToGet);
            }
            opusPacketFrame.Pop(framesToGet);
            return true;
        }

        /// <summary>
        /// 将16-bit little-endian PCM字节转换为归一化float（-1f~1f）。
        /// </summary>
        public static float[] Pcm16BytesToFloat(this byte[] pcmBytes)
        {
            if (pcmBytes == null || pcmBytes.Length == 0)
                return Array.Empty<float>();

            int sampleCount = pcmBytes.Length / 2;
            float[] floats = new float[sampleCount];
            ReadOnlySpan<byte> span = pcmBytes;
            for (int i = 0; i < sampleCount; i++)
            {
                short s = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i * 2, 2));
                floats[i] = s / 32768f;
            }
            return floats;
        }

        public static float[] PcmBytesToFloat(this byte[] pcmBytes, int bitDepth)
        {
            if (pcmBytes == null || pcmBytes.Length == 0)
                return Array.Empty<float>();

            return bitDepth switch
            {
                16 => pcmBytes.Pcm16BytesToFloat(),
                24 => Convert24BitPcm(pcmBytes),
                32 => Convert32BitPcm(pcmBytes),
                _ => throw new NotSupportedException($"Unsupported PCM bit depth: {bitDepth}")
            };

            static float[] Convert24BitPcm(byte[] bytes)
            {
                int sampleCount = bytes.Length / 3;
                float[] floats = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    int index = i * 3;
                    int value = bytes[index] | (bytes[index + 1] << 8) | (bytes[index + 2] << 16);
                    // 24-bit有符号：如果最高位(第23位)为1，需要符号扩展
                    if ((value & 0x800000) != 0)
                        value |= unchecked((int)0xFF000000);
                    floats[i] = value / 8388608f; // 2^23
                }
                return floats;
            }

            static float[] Convert32BitPcm(byte[] bytes)
            {
                int sampleCount = bytes.Length / 4;
                float[] floats = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    int raw = BitConverter.ToInt32(bytes, i * 4);
                    floats[i] = raw / 2147483648f; // 2^31
                }
                return floats;
            }
        }

        public static byte[] Float2PcmBytes(this float[] audioData, int bitDepth = 16, int channels = 1)
        {
            if (audioData == null || audioData.Length == 0)
                throw new ArgumentException(nameof(audioData));
            if (bitDepth is not (16 or 24 or 32))
                throw new ArgumentException("Only support 16-bit, 24-bit and 32-bit PCM format.");

            int sampleCount = audioData.Length / channels;
            int bytesPerSample = bitDepth / 8;
            int totalBytes = sampleCount * channels * bytesPerSample;
            var pcmData = new List<byte>(totalBytes);

            for (int i = 0; i < sampleCount; i++)
            {
                float sample = Math.Clamp(audioData[i], -1f, 1f);
                WriteSample(pcmData, sample, bitDepth);
            }
            return pcmData.ToArray();
        }

        private static void WriteSample(List<byte> pcmData, float sample, int bitDepth)
        {
            switch (bitDepth)
            {
                case 16:
                    short pcm16 = (short)(sample * 32767f);
                    pcmData.AddRange(BitConverter.GetBytes(pcm16));
                    break;
                case 24:
                    int pcm24 = (int)(sample * 8388607f);
                    pcmData.Add((byte)(pcm24 & 0xFF));
                    pcmData.Add((byte)((pcm24 >> 8) & 0xFF));
                    pcmData.Add((byte)((pcm24 >> 16) & 0xFF));
                    break;
                case 32:
                    int pcm32 = (int)(sample * 2147483647f);
                    pcmData.AddRange(BitConverter.GetBytes(pcm32));
                    break;
                default:
                    throw new ArgumentException("Unsupported bit depth: " + bitDepth);
            }
        }
    }
}
