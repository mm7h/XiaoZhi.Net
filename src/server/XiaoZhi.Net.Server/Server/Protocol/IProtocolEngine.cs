using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Protocol
{
    internal interface IProtocolEngine
    {
        bool Started { get; }
        void Build();
        Task StartAsync();
        Task StopAsync();
    }
}
