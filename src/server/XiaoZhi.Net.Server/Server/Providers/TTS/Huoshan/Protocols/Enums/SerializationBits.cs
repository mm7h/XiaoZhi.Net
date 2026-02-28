namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums
{
    /// <summary>
    /// Serialization bits defines the 4-bit serialization method type
    /// </summary>
    internal enum SerializationBits : byte
    {
        Raw = 0,
        JSON = 0b1,
        Thrift = 0b11,
        Custom = 0b1111
    }
}
