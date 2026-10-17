using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    internal sealed class ServerInfoAdapter : IServerInfo
    {
        public ServerInfoAdapter(string serverName, XiaoZhiConfig config)
        {
            this.ServerName = serverName;
            this.Config = config;
            this.Protocol = config.Protocol;
        }

        public string ServerName { get; }

        public XiaoZhiConfig Config { get; }

        public ServerProtocol Protocol { get; }
    }
}