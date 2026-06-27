using Microsoft.Extensions.Logging;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface IFunctionTool
    {
        ILogger Logger { get; }

        IServerInfo ServerInfo { get; }

        ISessionStore SessionStore { get; }

        ValueTask OnFunctionToolInitializedAsync();

        ValueTask OnFunctionToolReleasedAsync();
    }
}
