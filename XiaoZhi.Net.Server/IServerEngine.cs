using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server
{
    public interface IServerEngine
    {
        /// <summary>
        /// 是否已经启动
        /// </summary>
        bool Started { get; }
        /// <summary>
        /// 客户端连接存储管理
        /// 如果需要使用自定义的存储管理，请实现 IStore 接口
        /// </summary>
        IStore ConnectionStore { get; set; }
        /// <summary>
        /// 高级功能
        /// </summary>
        public IAdvanced Advanced { get; }
        /// <summary>
        /// 初始化服务引擎
        /// </summary>
        /// <param name="config">配置信息</param>
        /// <returns></returns>
        IServerEngine Initialize(XiaoZhiConfig config);
        /// <summary>
        /// 添加插件
        /// 需要在初始化服务引擎（Initialize）后调用
        /// </summary>
        /// <typeparam name="TPlugin">type of plugin object</typeparam>
        /// <param name="pluginName">插件名称</param>
        /// <returns></returns>
        IServerEngine WithPlugin<TPlugin>(string pluginName);
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
