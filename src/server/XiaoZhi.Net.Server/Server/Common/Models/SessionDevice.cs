using System.Net;

namespace XiaoZhi.Net.Server.Common.Models
{
    public class SessionDevice
    {
        public SessionDevice(string sessionId, string deviceId, IPEndPoint userEndPoint)
        {
            SessionId = sessionId;
            DeviceId = deviceId;
            EndPoint = userEndPoint;
        }
        /// <summary>
        /// 会话ID
        /// </summary>
        public string SessionId { get; }
        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId { get; }
        /// <summary>
        /// 客户端终端IP
        /// </summary>
        public IPEndPoint EndPoint { get; }
    }
}
