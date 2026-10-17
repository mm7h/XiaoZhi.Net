using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Enums;

namespace XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Models
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
            this.Version = VersionBits.Version1;
            this.HeaderSize = HeaderSizeBits.HeaderSize4;
            this.Serialization = SerializationBits.JSON;
            this.Compression = CompressionBits.None;
            this.Payload = Array.Empty<byte>();
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
            byte header1 = (byte)(((byte)this.Version << 4) | (byte)this.HeaderSize);
            byte header2 = (byte)(((byte)this.MsgType << 4) | (byte)this.MsgTypeFlag);
            byte header3 = (byte)(((byte)this.Serialization << 4) | (byte)this.Compression);

            stream.WriteByte(header1);
            stream.WriteByte(header2);
            stream.WriteByte(header3);

            // Write padding for header size
            int headerSize = 4 * (int)this.HeaderSize;
            int paddingSize = headerSize - 3;
            for (int i = 0; i < paddingSize; i++)
            {
                stream.WriteByte(0);
            }

            // Write fields in Go writers() order
            if ((this.MsgTypeFlag & MsgTypeFlagBits.WithEvent) != 0)
            {
                // Write event type
                var eventBytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(eventBytes, (int)this.EventType);
                stream.Write(eventBytes, 0, 4);

                // Write session ID
                this.WriteSessionId(stream);
            }

            // Write sequence if needed
            switch (this.MsgType)
            {
                case MsgType.FullClientRequest:
                case MsgType.FullServerResponse:
                case MsgType.FrontEndResultServer:
                case MsgType.AudioOnlyClient:
                case MsgType.AudioOnlyServer:
                    if (this.MsgTypeFlag == MsgTypeFlagBits.PositiveSeq || this.MsgTypeFlag == MsgTypeFlagBits.NegativeSeq)
                    {
                        var seqBytes = new byte[4];
                        BinaryPrimitives.WriteInt32BigEndian(seqBytes, this.Sequence);
                        stream.Write(seqBytes, 0, 4);
                    }
                    break;

                case MsgType.Error:
                    var errorBytes = new byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(errorBytes, this.ErrorCode);
                    stream.Write(errorBytes, 0, 4);
                    break;
            }

            // Write payload with length prefix
            this.WritePayload(stream);

            return stream.ToArray();
        }

        private void WriteSessionId(MemoryStream stream)
        {
            // Skip session ID for connection events
            switch (this.EventType)
            {
                case EventType.StartConnection:
                case EventType.FinishConnection:
                case EventType.ConnectionStarted:
                case EventType.ConnectionFailed:
                    return;
            }

            var sessionBytes = string.IsNullOrEmpty(this.SessionId) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(this.SessionId!);
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
            var payloadBytes = this.Payload ?? Array.Empty<byte>();
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
            this.Version = (VersionBits)(header1 >> 4);
            this.HeaderSize = (HeaderSizeBits)(header1 & 0x0F);

            int header2 = stream.ReadByte();
            this.MsgType = (MsgType)(header2 >> 4);
            this.MsgTypeFlag = (MsgTypeFlagBits)(header2 & 0x0F);

            int header3 = stream.ReadByte();
            this.Serialization = (SerializationBits)(header3 >> 4);
            this.Compression = (CompressionBits)(header3 & 0x0F);

            // Skip padding bytes
            int headerSize = 4 * (int)this.HeaderSize;
            int paddingSize = headerSize - 3;
            for (int i = 0; i < paddingSize; i++)
            {
                stream.ReadByte();
            }

            // Read fields in Go readers() order

            // First, read sequence or error code based on message type
            switch (this.MsgType)
            {
                case MsgType.FullClientRequest:
                case MsgType.FullServerResponse:
                case MsgType.FrontEndResultServer:
                case MsgType.AudioOnlyClient:
                case MsgType.AudioOnlyServer:
                    if (this.MsgTypeFlag == MsgTypeFlagBits.PositiveSeq || this.MsgTypeFlag == MsgTypeFlagBits.NegativeSeq)
                    {
                        var seqBytes = new byte[4];
                        stream.Read(seqBytes, 0, 4);
                        this.Sequence = BinaryPrimitives.ReadInt32BigEndian(seqBytes);
                    }
                    break;

                case MsgType.Error:
                    var errorBytes = new byte[4];
                    stream.Read(errorBytes, 0, 4);
                    this.ErrorCode = BinaryPrimitives.ReadUInt32BigEndian(errorBytes);
                    break;

                default:
                    throw new InvalidDataException($"Unsupported message type: {this.MsgType}");
            }

            // Then, if WithEvent flag is set, read event, session ID, and connect ID
            if ((this.MsgTypeFlag & MsgTypeFlagBits.WithEvent) != 0)
            {
                var eventBytes = new byte[4];
                stream.Read(eventBytes, 0, 4);
                this.EventType = (EventType)BinaryPrimitives.ReadInt32BigEndian(eventBytes);

                this.ReadSessionId(stream);
                this.ReadConnectId(stream);
            }

            // Read payload with length prefix
            this.ReadPayload(stream);

            // Verify no unexpected data remains
            if (stream.Position < stream.Length)
            {
                throw new InvalidDataException($"Unexpected data after message: {stream.Length - stream.Position} bytes remaining");
            }
        }

        private void ReadSessionId(MemoryStream stream)
        {
            // Skip session ID for connection events
            switch (this.EventType)
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
                this.SessionId = Encoding.UTF8.GetString(sessionBytes);
            }
        }

        private void ReadConnectId(MemoryStream stream)
        {
            // Only read connect ID for specific connection events
            switch (this.EventType)
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
                this.ConnectId = Encoding.UTF8.GetString(connectBytes);
            }
        }

        private void ReadPayload(MemoryStream stream)
        {
            var lenBytes = new byte[4];
            stream.Read(lenBytes, 0, 4);
            uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(lenBytes);

            if (payloadLength > 0)
            {
                this.Payload = new byte[payloadLength];
                stream.Read(this.Payload, 0, (int)payloadLength);
            }
            else
            {
                this.Payload = Array.Empty<byte>();
            }
        }

        public override string ToString()
        {
            switch (this.MsgType)
            {
                case MsgType.AudioOnlyServer:
                case MsgType.AudioOnlyClient:
                    if (this.MsgTypeFlag == MsgTypeFlagBits.PositiveSeq || this.MsgTypeFlag == MsgTypeFlagBits.NegativeSeq)
                    {
                        return $"SessionId: {this.SessionId}, ConnectId: {this.ConnectId}, MsgType: {this.MsgType}, EventType: {this.EventType}, Sequence: {this.Sequence}, PayloadSize: {this.Payload.Length}";
                    }
                    return $"SessionId: {this.SessionId}, ConnectId: {this.ConnectId}, MsgType: {this.MsgType}, EventType: {this.EventType}, PayloadSize: {this.Payload.Length}";

                case MsgType.Error:
                    return $"SessionId: {this.SessionId}, ConnectId: {this.ConnectId}, MsgType: {this.MsgType}, EventType: {this.EventType}, ErrorCode: {this.ErrorCode}, Payload: {this.GetPayloadString()}";

                default:
                    if (this.MsgTypeFlag == MsgTypeFlagBits.PositiveSeq || this.MsgTypeFlag == MsgTypeFlagBits.NegativeSeq)
                    {
                        return $"SessionId: {this.SessionId}, ConnectId: {this.ConnectId}, MsgType: {this.MsgType}, EventType: {this.EventType}, Sequence: {this.Sequence}, Payload: {this.GetPayloadString()}";
                    }
                    return $"SessionId: {this.SessionId}, ConnectId: {this.ConnectId}, MsgType: {this.MsgType}, EventType: {this.EventType}, Payload: {this.GetPayloadString()}";
            }
        }

        private string GetPayloadString()
        {
            if (this.Payload == null || this.Payload.Length == 0)
            {
                return "";
            }

            try
            {
                return Encoding.UTF8.GetString(this.Payload);
            }
            catch
            {
                return Convert.ToHexString(this.Payload);
            }
        }
    }
}
