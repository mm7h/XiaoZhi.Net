using Microsoft.Extensions.AI;
using System;

namespace XiaoZhi.Net.Server.Common.Models
{
    internal class Dialogue
    {
        public Dialogue(string deviceId, string clientSessionId, ChatRole role, string content)
        {
            this.DeviceId = deviceId;
            this.ClientSessionId = clientSessionId;
            this.Role = role;
            this.Content = content;
            this.CreateTime = DateTime.Now;
        }
        public string ClientSessionId { get; set; }
        public string DeviceId { get; }
        /// <summary>消息角色（user/assistant/system）</summary>
        public ChatRole Role { get; }
        public string Content { get; }
        public DateTime CreateTime { get; }
    }
}
