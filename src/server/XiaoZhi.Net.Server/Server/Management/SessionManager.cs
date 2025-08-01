using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Management
{
    internal sealed class SessionManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IStore _connectionStore;
        private readonly ILogger<SessionManager> _logger;

        public SessionManager(IServiceProvider serviceProvider, IStore store, ILogger<SessionManager> logger)
        {
            this._serviceProvider = serviceProvider;
            this._connectionStore = store;
            this._logger = logger;
        }
        public static IHostBuilder RegisterServices(IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<SessionManager>();
            });
        }
        public Session CreateSession(string sessionId, string deviceId, string authToken, IPEndPoint endPoint, IBizSendOutter sendOutter)
        {
            Session session = new Session(sessionId, deviceId, authToken, endPoint, sendOutter);
            session.HandlerPipeline.InitHandlerPipeline(this._serviceProvider, this._logger);
            return session;
        }

        public void AddSession(string sessionId, Session session)
        {
            this._connectionStore.Add(sessionId, session);
        }
        public IDictionary<string, SessionDevice> GetAllSessions()
        {
            return this._connectionStore.GetAll<Session>().ToDictionary(k => k.Key, v => new SessionDevice(v.Value.SessionId, v.Value.DeviceId, v.Value.EndPoint));
        }
        public Session GetSession(string sessionId)
        {
            return this._connectionStore.Get<Session>(sessionId);
        }
        public void UpdateSession(string sessionId, Session newSession)
        {
            this._connectionStore.Update(sessionId, newSession);
        }
        public void RemoveSession(string sessionId)
        {
            this._connectionStore.Remove(sessionId);
        }

    }
}
