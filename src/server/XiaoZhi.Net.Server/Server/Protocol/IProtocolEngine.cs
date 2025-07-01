using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Server.Protocol
{
    internal interface IProtocolEngine : ISendOutter
    {
        event Func<string, IDictionary<string, string>, IPEndPoint, bool> OnConnecting;
        event Action<string, string> OnTextMessage;
        event Action<string, byte[]> OnBinaryMessage;
        event Action<string> OnConnectionClose;
        bool Started { get; }
        void Build();
        Task StartAsync();
        Task StopAsync();

        void AddSessionContext(string connId, Session session);
        Session GetSessionContext(string connId);
        IDictionary<string, SessionDevice> GetAllSessions();
        void UpdateSessionContext(string connId, Session newSession);
        void RemoveSessionContext(string connId);
        void CloseSession(string connId);
    }
}
