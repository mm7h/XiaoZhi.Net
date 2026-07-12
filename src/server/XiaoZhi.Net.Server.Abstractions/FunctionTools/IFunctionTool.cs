using Microsoft.Extensions.Logging;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface IFunctionTool
    {
        /// <summary>
        /// 日志提供器
        /// </summary>
        ILogger Logger { get; }
        /// <summary>
        /// 当前服务端信息
        /// </summary>
        IServerInfo ServerInfo { get; }
        /// <summary>
        /// 当前服务端已连接的缓存
        /// </summary>
        ISessionStore SessionStore { get; }
        /// <summary>
        /// 当功能工具初始化完成时触发
        /// </summary>
        ValueTask OnFunctionToolInitializedAsync();
        /// <summary>
        /// 当功能工具释放时触发
        /// </summary>
        /// <returns></returns>
        ValueTask OnFunctionToolReleasedAsync();
    }
}
