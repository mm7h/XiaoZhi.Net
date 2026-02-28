using System;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums
{
    /// <summary>
    /// Message type flags which determines how the message will be serialized with the protocol
    /// </summary>
    [Flags]
    internal enum MsgTypeFlagBits : byte
    {
        NoSeq = 0,             // Non-terminal packet with no sequence
        PositiveSeq = 0b1,     // Non-terminal packet with sequence > 0
        LastNoSeq = 0b10,      // last packet with no sequence
        NegativeSeq = 0b11,    // last packet with sequence < 0
        WithEvent = 0b100      // Payload contains event number (int32)
    }
}
