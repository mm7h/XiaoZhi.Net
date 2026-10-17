using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Test.Helpers
{
    #region WaveHeader
    // Copyright (c)  2023  Xiaomi Corporation (authors: Fangjun Kuang)
    [StructLayout(LayoutKind.Sequential)]
    public struct WaveHeader
    {
        public int ChunkID;
        public int ChunkSize;
        public int Format;
        public int SubChunk1ID;
        public int SubChunk1Size;
        public short AudioFormat;
        public short NumChannels;
        public int SampleRate;
        public int ByteRate;
        public short BlockAlign;
        public short BitsPerSample;
        public int SubChunk2ID;
        public int SubChunk2Size;

        public bool Validate()
        {
            if (this.ChunkID != 0x46464952)
            {
                Console.WriteLine($"Invalid chunk ID: 0x{this.ChunkID:X}. Expect 0x46464952");
                return false;
            }

            //               E V A W
            if (this.Format != 0x45564157)
            {
                Console.WriteLine($"Invalid format: 0x{this.Format:X}. Expect 0x45564157");
                return false;
            }

            //                      t m f
            if (this.SubChunk1ID != 0x20746d66)
            {
                Console.WriteLine($"Invalid SubChunk1ID: 0x{this.SubChunk1ID:X}. Expect 0x20746d66");
                return false;
            }

            if (this.SubChunk1Size != 16)
            {
                Console.WriteLine($"Invalid SubChunk1Size: {this.SubChunk1Size}. Expect 16");
                return false;
            }

            if (this.AudioFormat != 1)
            {
                Console.WriteLine($"Invalid AudioFormat: {this.AudioFormat}. Expect 1");
                return false;
            }

            if (this.NumChannels != 1)
            {
                Console.WriteLine($"Invalid NumChannels: {this.NumChannels}. Expect 1");
                return false;
            }

            if (this.ByteRate != (this.SampleRate * this.NumChannels * this.BitsPerSample / 8))
            {
                Console.WriteLine($"Invalid byte rate: {this.ByteRate}.");
                return false;
            }

            if (this.BlockAlign != (this.NumChannels * this.BitsPerSample / 8))
            {
                Console.WriteLine($"Invalid block align: {this.ByteRate}.");
                return false;
            }

            if (this.BitsPerSample != 16)
            {  // we support only 16 bits per sample
                Console.WriteLine($"Invalid bits per sample: {this.BitsPerSample}. Expect 16");
                return false;
            }

            return true;
        }
    }

    // It supports only 16-bit, single channel WAVE format.
    // The sample rate can be any value.
    public class WaveReader
    {
        public WaveReader(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new ApplicationException($"{fileName} does not exist!");
            }

            using var stream = File.Open(fileName, FileMode.Open);
            using var reader = new BinaryReader(stream);

            this._header = ReadHeader(reader);

            if (!this._header.Validate())
            {
                throw new ApplicationException($"Invalid wave file ${fileName}");
            }

            this.SkipMetaData(reader);

            // now read samples
            // _header.SubChunk2Size contains number of bytes in total.
            // we assume each sample is of type int16
            var buffer = reader.ReadBytes(this._header.SubChunk2Size);
            var samples_int16 = new short[this._header.SubChunk2Size / 2];
            Buffer.BlockCopy(buffer, 0, samples_int16, 0, buffer.Length);

            this.Samples = new float[samples_int16.Length];

            for (var i = 0; i < samples_int16.Length; ++i)
            {
                this.Samples[i] = samples_int16[i] / 32768.0F;
            }
        }

        private static WaveHeader ReadHeader(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(Marshal.SizeOf(typeof(WaveHeader)));

            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            WaveHeader header = (WaveHeader)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(WaveHeader))!;
            handle.Free();

            return header;
        }

        private void SkipMetaData(BinaryReader reader)
        {
            var bs = reader.BaseStream;

            var subChunk2ID = this._header.SubChunk2ID;
            var subChunk2Size = this._header.SubChunk2Size;

            while (bs.Position != bs.Length && subChunk2ID != 0x61746164)
            {
                bs.Seek(subChunk2Size, SeekOrigin.Current);
                subChunk2ID = reader.ReadInt32();
                subChunk2Size = reader.ReadInt32();
            }
            this._header.SubChunk2ID = subChunk2ID;
            this._header.SubChunk2Size = subChunk2Size;
        }

        private WaveHeader _header;

        // Samples are normalized to the range [-1, 1]

        public int SampleRate => this._header.SampleRate;

        public float[] Samples { get; }

        public static void Test(string fileName)
        {
            WaveReader reader = new WaveReader(fileName);
            Console.WriteLine($"samples length: {reader.Samples.Length}");
            Console.WriteLine($"samples rate: {reader.SampleRate}");
        }
    }
    #endregion
}
