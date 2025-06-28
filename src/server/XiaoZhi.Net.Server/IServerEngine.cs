using System.Threading.Tasks;
using XiaoZhi.Net.Server.Management;

namespace XiaoZhi.Net.Server
{
    public interface IServerEngine
    {
        /// <summary>
        /// 是否已经启动
        /// </summary>
        bool Started { get; }
        /// <summary>
        /// 高级功能
        /// </summary>
        public IAdvanced Advanced { get; }
        /// <summary>
        /// 启动服务
        /// </summary>
        /// <returns></returns>
        Task StartAsync();
        /// <summary>
        /// 停止服务
        /// </summary>
        /// <returns></returns>
        Task StopAsync();
    }
}
