using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface IServerInfo
    {
        string ServerName { get; }
        XiaoZhiConfig Config { get; }
        public ServerProtocol Protocol { get; }
    }
}
