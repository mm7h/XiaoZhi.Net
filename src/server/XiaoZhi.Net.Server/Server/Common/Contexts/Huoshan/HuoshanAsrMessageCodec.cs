using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using XiaoZhi.Net.Server.Common.Contexts.Huoshan.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts.Huoshan
{
    /// <summary>
    /// Codec for the V3 SAUC WebSocket framing. It deliberately does not use the
    /// TTS Message class because ASR final audio uses flag 0b0011 (last + sequence).
    /// </summary>
    internal static class HuoshanAsrMessageCodec
    {
        private const byte PositiveSequence = 0b0001;
        private const byte LastWithSequence = 0b0011;

        public static byte[] CreateFullRequest(int sequence, byte[] jsonPayload)
            => CreateFrame(MsgType.FullClientRequest, PositiveSequence, sequence, jsonPayload);

        public static byte[] CreateAudioRequest(int sequence, byte[] audioPayload, bool isLast)
            => CreateFrame(MsgType.AudioOnlyClient, isLast ? LastWithSequence : PositiveSequence, isLast ? -sequence : sequence, audioPayload);

        public static HuoshanAsrResponse ParseResponse(byte[] data)
        {
            if (data is null || data.Length < 4)
            {
                throw new InvalidDataException("The Huoshan ASR response is shorter than its header.");
            }
            if ((data[0] >> 4) != (byte)VersionBits.Version1)
            {
                throw new InvalidDataException("The Huoshan ASR response uses an unsupported protocol version.");
            }

            int headerSize = data[0] & 0x0F;
            if (headerSize < 1 || data.Length < headerSize * 4)
            {
                throw new InvalidDataException("The Huoshan ASR response has an invalid header size.");
            }

            MsgType messageType = (MsgType)(data[1] >> 4);
            byte flags = (byte)(data[1] & 0x0F);
            SerializationBits serialization = (SerializationBits)(data[2] >> 4);
            CompressionBits compression = (CompressionBits)(data[2] & 0x0F);
            int offset = headerSize * 4;

            int sequence = 0;
            if ((flags & 0b0001) != 0)
            {
                EnsureAvailable(data, offset, 4);
                sequence = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                offset += 4;
            }

            int eventType = 0;
            if ((flags & 0b0100) != 0)
            {
                EnsureAvailable(data, offset, 4);
                eventType = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                offset += 4;
            }

            int errorCode = 0;
            if (messageType == MsgType.Error)
            {
                EnsureAvailable(data, offset, 4);
                errorCode = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                offset += 4;
            }

            int payloadLengthOffset = offset;
            if (messageType == MsgType.Error)
            {
                // Error frames place the error code before the payload length.
                payloadLengthOffset = offset;
            }

            EnsureAvailable(data, payloadLengthOffset, 4);
            uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payloadLengthOffset, 4));
            offset = payloadLengthOffset + 4;
            EnsureAvailable(data, offset, checked((int)payloadLength));
            byte[] payload = data.AsSpan(offset, checked((int)payloadLength)).ToArray();
            if (offset + payloadLength != data.Length)
            {
                throw new InvalidDataException("The Huoshan ASR response has trailing bytes after its payload.");
            }

            if (compression == CompressionBits.Gzip && payload.Length > 0)
            {
                payload = Decompress(payload);
            }

            JsonNode? jsonPayload = null;
            if (serialization == SerializationBits.JSON && payload.Length > 0)
            {
                jsonPayload = JsonNode.Parse(payload);
            }

            return new HuoshanAsrResponse(messageType, flags, sequence, eventType, errorCode, jsonPayload);
        }

        private static byte[] CreateFrame(MsgType messageType, byte flags, int sequence, byte[] payload)
        {
            byte[] compressedPayload = Compress(payload ?? Array.Empty<byte>());
            byte[] frame = new byte[12 + compressedPayload.Length];
            frame[0] = (byte)(((byte)VersionBits.Version1 << 4) | (byte)HeaderSizeBits.HeaderSize4);
            frame[1] = (byte)(((byte)messageType << 4) | flags);
            frame[2] = (byte)(((byte)SerializationBits.JSON << 4) | (byte)CompressionBits.Gzip);
            frame[3] = 0;
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4, 4), sequence);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), (uint)compressedPayload.Length);
            compressedPayload.CopyTo(frame, 12);
            return frame;
        }

        private static byte[] Compress(byte[] payload)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(payload, 0, payload.Length);
            }
            return output.ToArray();
        }

        private static byte[] Decompress(byte[] payload)
        {
            using var input = new MemoryStream(payload);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        private static void EnsureAvailable(byte[] data, int offset, int count)
        {
            if (offset < 0 || count < 0 || data.Length - offset < count)
            {
                throw new InvalidDataException("The Huoshan ASR response is truncated.");
            }
        }
    }
}
