namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface IPrivateFunctionTool : IFunctionTool
    {
        /// <summary>
        /// 当前Session会话上下文
        /// </summary>
        ISessionContext SessionContext { get; }
        /// <summary>
        /// 多媒体工具，用于处理音乐播放等功能
        /// </summary>
        IMediaTool MediaTool { get; }
        /// <summary>
        /// 当Session会话连接时触发
        /// </summary>
        /// <returns></returns>
        ValueTask OnSessionConnectedAsync();
        /// <summary>
        /// 当Session会话关闭时触发
        /// </summary>
        /// <returns></returns>
        ValueTask OnSessionClosedAsync();
    }
}
