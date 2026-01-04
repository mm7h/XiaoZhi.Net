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
            this.HandlerToken.Register(this.OnTokenCanceled);
            session.SessionCtsTokenChanged += this.OnSessionCtsTokenChanged;
        }

        protected bool CheckWorkflowValid<T>(Workflow<T> workflow)
        {
            // 为避免在触发Abort后，Channel中残留的旧Workflow被继续处理
            long sessionTurnId = this.SendOutter.GetSession().TurnId;
            if (workflow.TurnId != sessionTurnId)
            {
                this.Logger.LogDebug("Stale workflow detected in handler {handlerName}. Workflow turn ID: {workflowTurnId}, Session turn ID: {sessionTurnId}.", this.HandlerName, workflow.TurnId, sessionTurnId);
                return false;
            }
            return true;
        }

        protected virtual void OnHandlerTokenChanged()
        {
        }

        private void OnSessionCtsTokenChanged(CancellationToken newToken)
        {
            this._handlerCts?.Cancel();
            this._handlerCts = CancellationTokenSource.CreateLinkedTokenSource(newToken);
            this.HandlerToken = this._handlerCts.Token;
            this.HandlerToken.Register(this.OnTokenCanceled);
        }

        private void OnTokenCanceled()
        {
            this.Logger.LogDebug("Handler {handlerName} cancellation token has been canceled.", this.HandlerName);
            this.OnHandlerTokenChanged();
            Session session = this.SendOutter.GetSession();
            this.OnAbort?.Invoke(session.DeviceId, session.SessionId, this.HandlerName);
        }

        public virtual void Dispose()
        {
            Session session = this.SendOutter.GetSession();
            session.SessionCtsTokenChanged -= this.OnSessionCtsTokenChanged;
            this._handlerCts?.Dispose();
        }
    }
}
