using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions.Middleware;
using SuperSocket.Server.Abstractions.Session;
using SuperSocket.WebSocket;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol.WebSocket.Contexts;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Middlewares
{
    internal class SessionContainerMiddleware : MiddlewareBase, ISessionContainer
    {
        private readonly IStore _connectionStore;
        public SessionContainerMiddleware(IStore store)
        {
            this._connectionStore = store;
            this.Order = 1000;
        }

        public override ValueTask<bool> RegisterSession(IAppSession appSession)
        {
            if (appSession is IHandshakeRequiredSession handshakeSession)
            {
                if (!handshakeSession.Handshaked)
                    return ValueTask.FromResult(true);
            }

            if (appSession is SocketSession socketSession)
            {
                bool addResult = this._connectionStore.Add(appSession.SessionID, socketSession);

                if (!addResult)
                {
                    socketSession.Logger.LogWarning(Lang.SessionContainerMiddleware_RegisterSession_LoginFailed, socketSession.SessionID);
                    socketSession.CloseAsync(CloseReason.UnexpectedCondition, "Loggin failed");
                }
            }
            return ValueTask.FromResult(true);
        }

        public override ValueTask<bool> UnRegisterSession(IAppSession session)
        {
            this._connectionStore.Remove(session.SessionID);
            return ValueTask.FromResult(true);
        }

        public IAppSession GetSessionByID(string sessionId)
        {
            return this._connectionStore.Get<IAppSession>(sessionId);
        }

        public int GetSessionCount()
        {
            return this._connectionStore.GetAllCount();
        }

        public IEnumerable<IAppSession> GetSessions(Predicate<IAppSession> criteria)
        {
            return this._connectionStore.Get(criteria);
        }

        public IEnumerable<TAppSession> GetSessions<TAppSession>(Predicate<TAppSession> criteria) where TAppSession : IAppSession
        {
            return this._connectionStore.Get(criteria);
        }
    }
}
