using System;
using System.Net;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    internal sealed class SessionContextAdapter : ISessionContext
    {
        private readonly Session _session;

        public SessionContextAdapter(Session session)
        {
            this._session = session;
        }

        public string DeviceId => this._session.DeviceId;

        public string SessionId => this._session.SessionId;

        public DateTimeOffset LoginTime => this._session.LoginTime;

        public DateTimeOffset LastActiveTime => this._session.LastActivityTime;

        public EndPoint LocalEndPoint => this._session.LocalEndPoint;

        public EndPoint RemoteEndPoint => this._session.EndPoint;
    }
}
