using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.FunctionTools;

namespace XiaoZhi.Net.Server.Abstractions
{
    public abstract class FunctionTool : IFunctionTool
    {
        protected FunctionTool()
        {
        }

        public ILogger Logger { get; internal set; } = null!;

        public IServerInfo ServerInfo { get; internal set; } = null!;

        public ISessionStore SessionStore { get; internal set; } = null!;

        public virtual ValueTask OnFunctionToolInitializedAsync()
        {
            return ValueTask.CompletedTask;
        }

        public virtual ValueTask OnFunctionToolReleasedAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}