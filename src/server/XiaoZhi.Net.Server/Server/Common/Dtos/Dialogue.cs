using Microsoft.SemanticKernel.ChatCompletion;
using System;

namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class Dialogue
    {
        public Dialogue(string deviceId, string clientSessionId, AuthorRole role, string content)
        {
            DeviceId = deviceId;
            ClientSessionId = clientSessionId;
            Role = role;
            Content = content;
            CreateTime = DateTime.Now;
        }
        public string ClientSessionId { get; set; }
        public string DeviceId { get; }
        public AuthorRole Role { get; }
        public string Content { get; }
        public DateTime CreateTime { get; }
    }
}
