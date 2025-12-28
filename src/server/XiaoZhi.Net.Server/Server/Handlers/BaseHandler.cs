using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal abstract class BaseHandler : IHandler
    {
        private CancellationTokenSource? _handlerCts;

        public BaseHandler(XiaoZhiConfig config, ILogger logger)
        {
            this.Config = config;
            this.Logger = logger;
        }

        public XiaoZhiConfig Config { get; }
        public ILogger Logger { get; }

        public event Action<string, string, string>? OnAbort;
        public bool Builded { get; protected set; }
        public abstract string HandlerName { get; }
        public IBizSendOutter SendOutter { get; set; } = null!;
        protected CancellationToken HandlerToken { get; private set; }
        public abstract bool Build(PrivateProvider privateProvider);
        protected void RegisterCancellationToken()
        {
            Session session = this.SendOutter.GetSession();
            this._handlerCts = CancellationTokenSource.CreateLinkedTokenSource(session.SessionCtsToken);
            this.HandlerToken = this._handlerCts.Token;
            session.SessionCtsTokenChanged += this.OnSessionCtsTokenChanged;
        }
        protected void FireAbort(string deviceId, string sessionId, string currentHandler)
        {
            this.OnAbort?.Invoke(deviceId, sessionId, currentHandler);
        }
        public virtual void Dispose()
        {
            Session session = this.SendOutter.GetSession();
            session.SessionCtsTokenChanged -= this.OnSessionCtsTokenChanged;
            this._handlerCts?.Dispose();
        }

        private void OnSessionCtsTokenChanged(CancellationToken newToken)
        {
            this._handlerCts?.Cancel();
            this._handlerCts = CancellationTokenSource.CreateLinkedTokenSource(newToken);
            this.HandlerToken = this._handlerCts.Token;
        }
    }
}
