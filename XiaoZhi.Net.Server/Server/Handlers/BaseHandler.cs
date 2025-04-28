using Serilog;
using System;

namespace XiaoZhi.Net.Server.Handlers
{
    internal abstract class BaseHandler
    {
        public BaseHandler(XiaoZhiConfig config, ILogger logger)
        {
            this.Config = config;
            this.Logger = logger;
        }

        public XiaoZhiConfig Config { get; }
        public ILogger Logger { get; }

        public event Action<string, string, string> OnAbort;

        public abstract string HandlerName { get; }

        protected void FireAbort(string deviceId, string sessionId, string currentHandler)
        {
            this.OnAbort?.Invoke(deviceId, sessionId, currentHandler);
        }
    }
}
