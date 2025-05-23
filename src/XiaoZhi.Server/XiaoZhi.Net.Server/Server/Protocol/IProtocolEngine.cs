using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Models;

namespace XiaoZhi.Net.Server.Protocol
{
    internal interface IProtocolEngine : ISendOutter
    {
        bool Started { get; }
        IProtocolService Service { get; }
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
