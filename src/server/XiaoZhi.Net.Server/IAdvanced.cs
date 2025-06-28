using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Models;

namespace XiaoZhi.Net.Server
{
    public interface IAdvanced
    {
        /// <summary>
        /// 获取所有客户端连接
        /// </summary>
        /// <returns>客户端设备信息列表</returns>
        IDictionary<string, SessionDevice> GetAllSessions();
        /// <summary>
        /// 发送自定义消息
        /// 如果客户端不在线，则会丢弃消息
        /// </summary>
        /// <param name="sessionId"></param>
        /// <param name="content"></param>
        /// <returns></returns>
        Task SendCustomMessage(string sessionId, string content);
    }
}
