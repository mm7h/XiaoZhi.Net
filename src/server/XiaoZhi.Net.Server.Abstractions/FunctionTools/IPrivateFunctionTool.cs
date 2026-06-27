namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface IPrivateFunctionTool : IFunctionTool
    {
        ISessionContext SessionContext { get; }
        IMediaTool MediaTool { get; }
        ValueTask OnSessionConnectedAsync();

        ValueTask OnSessionClosedAsync();
    }
}
