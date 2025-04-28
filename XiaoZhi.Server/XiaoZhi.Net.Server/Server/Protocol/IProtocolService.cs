using System;
using System.Collections.Generic;
using System.Net;

namespace XiaoZhi.Net.Server.Protocol
{
    internal interface IProtocolService
    {
        event Func<string, IDictionary<string, string>, IPEndPoint, bool> OnConnecting;
        event Action<string, string> OnTextMessage;
        event Action<string, byte[]> OnBinaryMessage;
        event Action<string> OnConnectionClose;
    }
}
