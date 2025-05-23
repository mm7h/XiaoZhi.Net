using System;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server
{
    public interface IServerBuilder
    {
        /// <summary>
        /// 初始化服务
        /// </summary>
        /// <param name="config">配置信息</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        IServerBuilder Initialize(XiaoZhiConfig config);
        /// <summary>
        /// 初始化服务
        /// </summary>
        /// <param name="config">配置信息</param>
        /// <param name="connectionStore">自定义的连接信息存储管理器</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        IServerBuilder Initialize(XiaoZhiConfig config, IStore connectionStore);
        /// <summary>
        /// 添加插件
        /// </summary>
        /// <typeparam name="TPlugin">插件类对应的Type</typeparam>
        /// <param name="pluginName">插件名称</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        IServerBuilder WithPlugin<TPlugin>(string pluginName);
        /// <summary>
        /// 添加插件
        /// </summary>
        /// <typeparam name="TPlugin">插件类对应的Type</typeparam>
        /// <param name="pluginName">插件名称</param>
        /// <param name="functions">支撑该插件的functions</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        IServerBuilder WithPlugin<TPlugin>(string pluginName, IEnumerable<IFunction> functions);
        /// <summary>
        /// 构建服务引擎
        /// </summary>
        /// <returns></returns>
        IServerEngine Build();
    }
}
