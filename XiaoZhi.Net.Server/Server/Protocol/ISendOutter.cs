using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Protocol
{
    internal interface ISendOutter
    {
        Task SendAsync(string sessionId, string json);
        Task SendAsync(string sessionId, byte[] opusPacket);
        Task SendTtsMessageAsync(string sessionId, string state, string? text = null);
        Task SendSttMessageAsync(string sessionId, string sttText);
        Task SendLlmMessageAsync(string sessionId, Emotion emotion);
    }
}
