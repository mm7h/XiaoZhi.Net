using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    internal sealed class SessionControllerAdapter : ISessionController
    {
        private readonly Session _session;

        public SessionControllerAdapter(Session session)
        {
            this._session = session;
        }

        public bool TryRequestCloseAfterChat()
        {
            return this._session.TryRequestCloseAfterChat();
        }
    }
}
