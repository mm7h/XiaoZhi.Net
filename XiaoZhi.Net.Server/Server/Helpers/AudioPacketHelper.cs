using Serilog;
using SherpaOnnx;
using System;
using System.Collections.Generic;
using System.Text;

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
                //if (opusPacketFrame.Size == 0)
                //{
                //    data = Array.Empty<float>();
                //    return false;
                //}
                //if (opusPacketFrame.Size != 0)
                //{
                //    if (opusPacketFrame.Size < size)
                //    {
                //        data = opusPacketFrame.Get(opusPacketFrame.Head, opusPacketFrame.Size);
                //        opusPacketFrame.Pop(opusPacketFrame.Size);
                //        return true;
                //    }
                //    else
                //    {
                //        data = opusPacketFrame.Get(opusPacketFrame.Head, size);
                //        opusPacketFrame.Pop(size);
                //        return true;
                //    }
                //}
                //else
                //{
                //    data = Array.Empty<float>();
                //    return false;
                //}
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GetFrames failed.");
                Log.Error("GetFrames failed.");
                data = Array.Empty<float>();
                return false;
            }
        }


        public static float[] Bytes2Float(this byte[] opusBytes)
        {
            if (opusBytes == null || opusBytes.Length == 0)
                return Array.Empty<float>();

            // 计算是否需要填充字节
            int remainder = opusBytes.Length % 4;
            byte[] paddedBytes = opusBytes;

            // 如果长度不是4的倍数，则进行填充
            if (remainder != 0)
            {
                int paddedLength = opusBytes.Length + (4 - remainder);
                paddedBytes = new byte[paddedLength];
                Array.Copy(opusBytes, paddedBytes, opusBytes.Length);
                // 剩余部分默认为0，不需要显式填充
            }

            // 计算浮点数数组大小
            int floatCount = paddedBytes.Length / 4;
            float[] floats = new float[floatCount];

            // 转换为浮点数
            for (int i = 0; i < floatCount; i++)
            {
                floats[i] = BitConverter.ToSingle(paddedBytes, i * 4);
            }

            return floats;
        }

        public static byte[] Float2PcmBytes(this float[] audioData, int bitDepth = 16, int channels = 1)
        {
            if (audioData == null || audioData.Length == 0)
                throw new ArgumentException(nameof(audioData));

            if (bitDepth != 16 && bitDepth != 24 && bitDepth != 32)
                throw new ArgumentException("Only support 16-bit, 24-bit and 32-bit PCM format.");

            int sampleCount = audioData.Length / channels; // 每个声道的样本数量
            int bytesPerSample = bitDepth / 8;             // 每个样本占用的字节数
            int totalBytes = sampleCount * channels * bytesPerSample; // 总字节数

            List<byte> pcmData = new List<byte>(totalBytes);

            for (int i = 0; i < sampleCount; i++)
            {
                float sample = Math.Clamp(audioData[i], -1.0f, 1.0f);
                WriteSample(pcmData, sample, bitDepth);
            }

            return pcmData.ToArray();
        }

        /// <summary>
        /// 将单个样本写入 PCM 数据流
        /// </summary>
        /// <param name="pcmData">用于存储 PCM 数据的 List<byte></param>
        /// <param name="sample">归一化的浮点样本值（-1.0 到 1.0）</param>
        /// <param name="bitDepth">位深度（16、24 或 32）</param>
        private static void WriteSample(List<byte> pcmData, float sample, int bitDepth)
        {
            switch (bitDepth)
            {
                case 16:
                    // 缩放到 16-bit 范围
                    short pcm16 = (short)(sample * 32767.0f);
                    pcmData.AddRange(BitConverter.GetBytes(pcm16)); // 小端模式
                    break;
                case 24:
                    // 缩放到 24-bit 范围
                    int pcm24 = (int)(sample * 8388607.0f); // 2^23 - 1
                    pcmData.Add((byte)(pcm24 & 0xFF));         // 低字节
                    pcmData.Add((byte)((pcm24 >> 8) & 0xFF));  // 中字节
                    pcmData.Add((byte)((pcm24 >> 16) & 0xFF)); // 高字节
                    break;
                case 32:
                    // 直接写入 IEEE 754 浮点数
                    pcmData.AddRange(BitConverter.GetBytes(sample)); // 小端模式
                    break;
                default:
                    throw new ArgumentException("Unsupported bit depth: " + bitDepth);
            }
        }
    }
}
