using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Protocol
{
    internal interface IBizSendOutter : ISocketSendOutter
    {
        string SessionId { get; }
        Session GetSession();
        Task SendTtsMessageAsync(string state, string? text = null);
        Task SendSttMessageAsync(string sttText);
        Task SendLlmMessageAsync(Emotion emotion);
        Task SendAbortMessageAsync();
        Task CloseSessionAsync(string reason = "");
    }
}
