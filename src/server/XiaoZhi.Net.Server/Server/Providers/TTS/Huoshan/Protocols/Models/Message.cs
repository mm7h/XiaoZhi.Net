using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models
{
    /// <summary>
    /// Message structure for protocol communication
    /// </summary>
    ///   0                 1                 2                 3
    /// | 0 1 2 3 4 5 6 7 | 0 1 2 3 4 5 6 7 | 0 1 2 3 4 5 6 7 | 0 1 2 3 4 5 6 7 |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |    Version      |   Header Size   |     Msg Type    |      Flags      |
    /// |   (4 bits)      |    (4 bits)     |     (4 bits)    |     (4 bits)    |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// | Serialization   |   Compression   |           Reserved                |
    /// |   (4 bits)      |    (4 bits)     |           (8 bits)                |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |                                                                       |
    /// |                   Optional Header Extensions                          |
    /// |                     (if Header Size > 1)                              |
    /// |                                                                       |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |                                                                       |
    /// |                           Payload                                     |
    /// |                      (variable length)                                |
    /// |                                                                       |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    internal class Message
    {
        public VersionBits Version { get; set; }
        public HeaderSizeBits HeaderSize { get; set; }
        public MsgType MsgType { get; set; }
        public MsgTypeFlagBits MsgTypeFlag { get; set; }
        public SerializationBits Serialization { get; set; }
        public CompressionBits Compression { get; set; }
        public EventType EventType { get; set; }
        public string? SessionId { get; set; }
        public string? ConnectId { get; set; }
        public int Sequence { get; set; }
        public uint ErrorCode { get; set; }

        public byte[] Payload { get; set; }

        /// <summary>
        /// Creates a new message with default values
        /// </summary>
        public Message()
        {
            Version = VersionBits.Version1;
            HeaderSize = HeaderSizeBits.HeaderSize4;
            Serialization = SerializationBits.JSON;
            Compression = CompressionBits.None;
            Payload = Array.Empty<byte>();
        }

        /// <summary>
        /// Creates a new message with specified message type and flag
        /// </summary>
        public static Message Create(MsgType msgType, MsgTypeFlagBits flag)
        {
            return new Message
            {
                MsgType = msgType,
                MsgTypeFlag = flag
            };
        }

        /// <summary>
        /// Creates a message from byte array
        /// </summary>
        public static Message FromBytes(byte[] data)
        {
            if (data == null || data.Length < 4)
            {
                throw new ArgumentException("Invalid data length", nameof(data));
            }

            var message = new Message();
            using var stream = new MemoryStream(data);
            message.Unmarshal(stream);
            return message;
        }

        /// <summary>
        /// Converts the message to a byte array
        /// </summary>
        public byte[] Marshal()
        {
            using var stream = new MemoryStream();

            // Write header bytes
            byte header1 = (byte)((byte)Version << 4 | (byte)HeaderSize);
            byte header2 = (byte)((byte)MsgType << 4 | (byte)MsgTypeFlag);
            byte header3 = (byte)((byte)Serialization << 4 | (byte)Compression);

            stream.WriteByte(header1);
            stream.WriteByte(header2);
            stream.WriteByte(header3);

            // Write padding for header size
            int headerSize = 4 * (int)HeaderSize;
            int paddingSize = headerSize - 3;
            for (int i = 0; i < paddingSize; i++)
            {
                stream.WriteByte(0);
            }

            // Write fields in Go writers() order
            if ((MsgTypeFlag & MsgTypeFlagBits.WithEvent) != 0)
            {
                // Write event type
                var eventBytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(eventBytes, (int)EventType);
                stream.Write(eventBytes, 0, 4);

                // Write session ID
                WriteSessionId(stream);
            }

            // Write sequence if needed
            switch (MsgType)
            {
                case MsgType.FullClientRequest:
                case MsgType.FullServerResponse:
                case MsgType.FrontEndResultServer:
                case MsgType.AudioOnlyClient:
                case MsgType.AudioOnlyServer:
                    if (MsgTypeFlag == MsgTypeFlagBits.PositiveSeq || MsgTypeFlag == MsgTypeFlagBits.NegativeSeq)
                    {
                        var seqBytes = new byte[4];
                        BinaryPrimitives.WriteInt32BigEndian(seqBytes, Sequence);
                        stream.Write(seqBytes, 0, 4);
                    }
                    break;

                case MsgType.Error:
                    var errorBytes = new byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(errorBytes, ErrorCode);
                    stream.Write(errorBytes, 0, 4);
                    break;
            }

            // Write payload with length prefix
            WritePayload(stream);

            return stream.ToArray();
        }

        private void WriteSessionId(MemoryStream stream)
        {
            // Skip session ID for connection events
            switch (EventType)
            {
                case EventType.StartConnection:
                case EventType.FinishConnection:
                case EventType.ConnectionStarted:
                case EventType.ConnectionFailed:
                    return;
            }

            var sessionBytes = string.IsNullOrEmpty(SessionId) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(SessionId!);
            var lenBytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(lenBytes, (uint)sessionBytes.Length);
            stream.Write(lenBytes, 0, 4);
            if (sessionBytes.Length > 0)
            {
                stream.Write(sessionBytes, 0, sessionBytes.Length);
            }
        }

        private void WritePayload(MemoryStream stream)
        {
            var payloadBytes = Payload ?? Array.Empty<byte>();
            var lenBytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(lenBytes, (uint)payloadBytes.Length);
            stream.Write(lenBytes, 0, 4);
            if (payloadBytes.Length > 0)
            {
                stream.Write(payloadBytes, 0, payloadBytes.Length);
            }
        }

        /// <summary>
        /// Unmarshals a byte array into the message
        /// </summary>
        private void Unmarshal(MemoryStream stream)
        {
            // Read header bytes
            int header1 = stream.ReadByte();
            Version = (VersionBits)(header1 >> 4);
            HeaderSize = (HeaderSizeBits)(header1 & 0x0F);

            int header2 = stream.ReadByte();
            MsgType = (MsgType)(header2 >> 4);
            MsgTypeFlag = (MsgTypeFlagBits)(header2 & 0x0F);

            int header3 = stream.ReadByte();
            Serialization = (SerializationBits)(header3 >> 4);
            Compression = (CompressionBits)(header3 & 0x0F);

            // Skip padding bytes
            int headerSize = 4 * (int)HeaderSize;
            int paddingSize = headerSize - 3;
            for (int i = 0; i < paddingSize; i++)
            {
                stream.ReadByte();
            }

            // Read fields in Go readers() order

            // First, read sequence or error code based on message type
            switch (MsgType)
            {
                case MsgType.FullClientRequest:
                case MsgType.FullServerResponse:
                case MsgType.FrontEndResultServer:
                case MsgType.AudioOnlyClient:
                case MsgType.AudioOnlyServer:
                    if (MsgTypeFlag == MsgTypeFlagBits.PositiveSeq || MsgTypeFlag == MsgTypeFlagBits.NegativeSeq)
                    {
                        var seqBytes = new byte[4];
                        stream.Read(seqBytes, 0, 4);
                        Sequence = BinaryPrimitives.ReadInt32BigEndian(seqBytes);
                    }
                    break;

                case MsgType.Error:
                    var errorBytes = new byte[4];
                    stream.Read(errorBytes, 0, 4);
                    ErrorCode = BinaryPrimitives.ReadUInt32BigEndian(errorBytes);
                    break;

                default:
                    throw new InvalidDataException($"Unsupported message type: {MsgType}");
            }

            // Then, if WithEvent flag is set, read event, session ID, and connect ID
            if ((MsgTypeFlag & MsgTypeFlagBits.WithEvent) != 0)
            {
                var eventBytes = new byte[4];
                stream.Read(eventBytes, 0, 4);
                EventType = (EventType)BinaryPrimitives.ReadInt32BigEndian(eventBytes);

                ReadSessionId(stream);
                ReadConnectId(stream);
            }

            // Read payload with length prefix
            ReadPayload(stream);

            // Verify no unexpected data remains
            if (stream.Position < stream.Length)
            {
                throw new InvalidDataException($"Unexpected data after message: {stream.Length - stream.Position} bytes remaining");
            }
        }

        private void ReadSessionId(MemoryStream stream)
        {
            // Skip session ID for connection events
            switch (EventType)
            {
                case EventType.StartConnection:
                case EventType.FinishConnection:
                case EventType.ConnectionStarted:
                case EventType.ConnectionFailed:
                case EventType.ConnectionFinished:
                    return;
            }

            var lenBytes = new byte[4];
            stream.Read(lenBytes, 0, 4);
            uint sessionIdLength = BinaryPrimitives.ReadUInt32BigEndian(lenBytes);

            if (sessionIdLength > 0)
            {
                var sessionBytes = new byte[sessionIdLength];
                stream.Read(sessionBytes, 0, (int)sessionIdLength);
                SessionId = Encoding.UTF8.GetString(sessionBytes);
            }
        }

        private void ReadConnectId(MemoryStream stream)
        {
            // Only read connect ID for specific connection events
            switch (EventType)
            {
                case EventType.ConnectionStarted:
                case EventType.ConnectionFailed:
                case EventType.ConnectionFinished:
                    break;
                default:
                    return;
            }

            var lenBytes = new byte[4];
            stream.Read(lenBytes, 0, 4);
            uint connectIdLength = BinaryPrimitives.ReadUInt32BigEndian(lenBytes);

            if (connectIdLength > 0)
            {
                var connectBytes = new byte[connectIdLength];
                stream.Read(connectBytes, 0, (int)connectIdLength);
                ConnectId = Encoding.UTF8.GetString(connectBytes);
            }
        }

        private void ReadPayload(MemoryStream stream)
        {
            var lenBytes = new byte[4];
            stream.Read(lenBytes, 0, 4);
            uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(lenBytes);

            if (payloadLength > 0)
            {
                Payload = new byte[payloadLength];
                stream.Read(Payload, 0, (int)payloadLength);
            }
            else
            {
                Payload = Array.Empty<byte>();
            }
        }

        public override string ToString()
        {
            switch (MsgType)
            {
                case MsgType.AudioOnlyServer:
                case MsgType.AudioOnlyClient:
                    if (MsgTypeFlag == MsgTypeFlagBits.PositiveSeq || MsgTypeFlag == MsgTypeFlagBits.NegativeSeq)
                    {
                        return $"SessionId: {SessionId}, ConnectId: {ConnectId}, MsgType: {MsgType}, EventType: {EventType}, Sequence: {Sequence}, PayloadSize: {Payload.Length}";
                    }
                    return $"SessionId: {SessionId}, ConnectId: {ConnectId}, MsgType: {MsgType}, EventType: {EventType}, PayloadSize: {Payload.Length}";

                case MsgType.Error:
                    return $"SessionId: {SessionId}, ConnectId: {ConnectId}, MsgType: {MsgType}, EventType: {EventType}, ErrorCode: {ErrorCode}, Payload: {GetPayloadString()}";

                default:
                    if (MsgTypeFlag == MsgTypeFlagBits.PositiveSeq || MsgTypeFlag == MsgTypeFlagBits.NegativeSeq)
                    {
                        return $"SessionId: {SessionId}, ConnectId: {ConnectId}, MsgType: {MsgType}, EventType: {EventType}, Sequence: {Sequence}, Payload: {GetPayloadString()}";
                    }
                    return $"SessionId: {SessionId}, ConnectId: {ConnectId}, MsgType: {MsgType}, EventType: {EventType}, Payload: {GetPayloadString()}";
            }
        }

        private string GetPayloadString()
        {
            if (Payload == null || Payload.Length == 0)
                return "";
            try
            {
                return Encoding.UTF8.GetString(Payload);
            }
            catch
            {
                return Convert.ToHexString(Payload);
            }
        }
    }

}