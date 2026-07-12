using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface IServerInfo
    {
        /// <summary>
        /// 当前服务端的名称
        /// </summary>
        string ServerName { get; }
        /// <summary>
        /// 当前服务端配置
        /// 对属性进行修改不会影响实际配置
        /// </summary>
        XiaoZhiConfig Config { get; }
        /// <summary>
        /// 当前服务端协议
        /// </summary>
        public ServerProtocol Protocol { get; }
    }
}
