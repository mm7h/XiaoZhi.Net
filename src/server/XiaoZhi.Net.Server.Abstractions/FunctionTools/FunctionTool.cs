using Microsoft.Extensions.Logging;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public abstract class FunctionTool
    {
        protected FunctionTool()
        {

        }
        public ILogger Logger { get; internal set; }
        public ISessionContext SessionContext { get; internal set; }
        public IServerInfo ServerInfo { get; internal set; }
        public ISessionStore SessionStore { get; internal set; }
        public IMediaTool MediaTool { get; internal set; }
        protected virtual ValueTask OnFunctionToolInitializedAsync()
        {
            return ValueTask.CompletedTask;
        }
        protected virtual ValueTask OnSessionConnectedAsync()
        {
            return ValueTask.CompletedTask;
        }
        protected virtual ValueTask OnSessionClosedAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
