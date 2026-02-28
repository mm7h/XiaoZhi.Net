using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums
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
