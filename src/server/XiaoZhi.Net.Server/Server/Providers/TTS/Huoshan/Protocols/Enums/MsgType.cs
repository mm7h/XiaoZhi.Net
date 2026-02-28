namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums
{
    /// <summary>
    /// Message type which determines how the message will be serialized with the protocol
    /// </summary>
    internal enum MsgType : byte
    {
        Invalid = 0,
        FullClientRequest = 0b1,
        AudioOnlyClient = 0b10,
        FullServerResponse = 0b1001,
        AudioOnlyServer = 0b1011,
        FrontEndResultServer = 0b1100,
        Error = 0b1111,

        ServerACK = AudioOnlyServer
    }
}
