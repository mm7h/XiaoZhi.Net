using Microsoft.Extensions.Logging;
using System;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal abstract class BaseHandler : IHandler
    {
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

        public abstract bool Build(PrivateProvider privateProvider);
        protected void FireAbort(string deviceId, string sessionId, string currentHandler)
        {
            this.OnAbort?.Invoke(deviceId, sessionId, currentHandler);
        }
        public virtual void Dispose()
        { }
    }
}
