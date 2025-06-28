using Microsoft.SemanticKernel.ChatCompletion;
using System;

namespace XiaoZhi.Net.Server.Common.Entities
{
    internal class Dialogue
    {
        public Dialogue() { }
        public Dialogue(string deviceId, string clientSessionId, AuthorRole role, string content) 
        {
            this.DeviceId = deviceId;
            this.ClientSessionId = clientSessionId;
            this.Role = role;
            this.Content = content;
            this.CreateTime = DateTime.Now;
        }
        public string ClientSessionId { get; set; }
        public string DeviceId { get; set; }
        public AuthorRole Role { get; set; }
        public string Content { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
