using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal interface IHandler
    {
        IBizSendOutter SendOutter { get; set; }
    }
}
