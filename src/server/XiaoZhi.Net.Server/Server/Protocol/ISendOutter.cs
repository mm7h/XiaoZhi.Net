using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Protocol
{
    internal interface ISendOutter
    {
        string SessionId { get; }
        Session GetSession();
        Task SendAsync(string json);
        Task SendAsync(byte[] opusPacket);
        Task SendTtsMessageAsync(string state, string? text = null);
        Task SendSttMessageAsync(string sttText);
        Task SendLlmMessageAsync(Emotion emotion);
        Task SendAbortMessageAsync();
        Task CloseSessionAsync(string reason = "");
    }
}
