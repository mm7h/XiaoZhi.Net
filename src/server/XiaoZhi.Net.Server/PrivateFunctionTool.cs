using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.FunctionTools;

namespace XiaoZhi.Net.Server.Abstractions
{
    public abstract class PrivateFunctionTool : FunctionTool, IPrivateFunctionTool
    {
        protected PrivateFunctionTool()
        {
        }

        public ISessionContext SessionContext { get; internal set; } = null!;

        public ISessionController SessionController { get; internal set; } = null!;

        public IMediaTool MediaTool { get; internal set; } = null!;

        public virtual ValueTask OnSessionConnectedAsync()
        {
            return ValueTask.CompletedTask;
        }

        public virtual ValueTask OnSessionClosedAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
