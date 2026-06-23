using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface IServerInfo
    {
        string Name { get; }
        XiaoZhiConfig Config { get; }
        string Path { get; }
        public ServerProtocol Protocol { get; }
    }
}
