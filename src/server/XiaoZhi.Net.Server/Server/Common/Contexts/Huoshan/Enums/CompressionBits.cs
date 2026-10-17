namespace XiaoZhi.Net.Server.Common.Contexts.Huoshan.Enums
{
    /// <summary>
    /// Compression bits defines the 4-bit compression method type
    /// </summary>
    internal enum CompressionBits : byte
    {
        None = 0,
        Gzip = 0b1,
        Custom = 0b1111
    }
}
