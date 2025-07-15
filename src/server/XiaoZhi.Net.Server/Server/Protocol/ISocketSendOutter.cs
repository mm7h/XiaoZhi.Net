using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Protocol
{
    internal interface ISocketSendOutter
    {
        Task SendAsync(string json);
        Task SendAsync(byte[] bytePacket);
    }
}
