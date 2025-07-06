using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal interface IHandler
    {
        ISendOutter SendOutter { get; set; }
    }
}
