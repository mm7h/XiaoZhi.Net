using System.Net;

namespace XiaoZhi.Net.Server.Abstractions
{
    public interface IBasicVerify
    {
        /// <summary>
        /// 客户端验证。
        /// </summary>
        /// <param name="deviceId">设备Id</param>
        /// <param name="token">token</param>
        /// <param name="userEndPoint">用户登录来源地址</param>
        /// <returns>是否允许设备连接到服务器</returns>
        bool Verify(string deviceId, string token, IPEndPoint userEndPoint);
    }
}
