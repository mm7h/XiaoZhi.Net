using SuperSocket.Server.Abstractions.Session;
using XiaoZhi.Net.Server.Protocol.WebSocket.Contexts;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    internal sealed class SessionStoreAdapter : ISessionStore
    {
        private readonly ISessionContainer _sessionContainer;

        public SessionStoreAdapter(ISessionContainer sessionContainer)
        {
            this._sessionContainer = sessionContainer;
        }

        public ISessionContext GetSession(string sessionId)
        {
            SocketSession socketSession = (SocketSession)this._sessionContainer.GetSessionByID(sessionId);
            return new SessionContextAdapter(socketSession.GetSession());
        }

        public long GetSessionCount()
        {
            return this._sessionContainer.GetSessionCount();
        }
    }
}