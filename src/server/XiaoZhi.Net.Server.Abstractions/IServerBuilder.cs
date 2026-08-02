using Microsoft.Extensions.Hosting;
using System.Globalization;
using XiaoZhi.Net.Server.Abstractions.FunctionTools;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.RAG.Abstractions;

namespace XiaoZhi.Net.Server.Abstractions
{
    public interface IServerBuilder
    {
        /// <summary>
        /// 获取当前的HostBuilder
        /// </summary>
        IHostBuilder HostBuilder { get; }
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
        /// 注册自定义函数工具。
        /// </summary>
        /// <typeparam name="TFunctionTool">需要注册的函数工具类型。</typeparam>
        /// <returns></returns>
        IServerBuilder WithFunctionTools<TFunctionTool>() where TFunctionTool : class, IFunctionTool, new();
        /// <summary>
        /// 注册自定义函数工具。
        /// </summary>
        /// <typeparam name="TFunctionTool">需要注册的函数工具类型。</typeparam>
        /// <returns></returns>
        IServerBuilder WithPrivateFunctionTools<TFunctionTool>() where TFunctionTool : class, IPrivateFunctionTool, new ();
        /// <summary>
        /// 添加自定义验证
        /// </summary>
        /// <returns></returns>
        IServerBuilder WithVerify<T>() where T : class, IBasicVerify;
        /// <summary>
        /// 添加管理API配置
        /// </summary>
        /// <param name="manageApiUrl">url</param>
        /// <param name="secret">密钥</param>
        /// <returns></returns>
        IServerBuilder WithManageApi(string manageApiUrl, string secret);
        /// <summary>
        /// 为 RAG 配置注册自定义向量存储
        /// </summary>
        IServerBuilder WithRagVectorStore<TVectorStore>() where TVectorStore : class, IVectorStore;

        /// <summary>
        /// 为 RAG 配置注册自定义向量存储
        /// </summary>
        IServerBuilder WithRagVectorStore(IVectorStore vectorStore);
        /// <summary>
        /// 设置默认语言信息
        /// 这会涉及到日志输出、错误信息等的本地化
        /// 语言/区域（culture）的简写遵循 RFC 4646标准
        /// </summary>
        /// <param name="culture">语言/区域名称</param>
        /// <returns></returns>
        IServerBuilder WithCulture(string culture = "zh-CN");
        /// <summary>
        /// 设置默认语言信息
        /// 这会涉及到日志输出、错误信息等的本地化
        /// 语言/区域（culture）的简写遵循 RFC 4646标准
        /// </summary>
        /// <param name="culture">语言/区域</param>
        /// <returns></returns>
        IServerBuilder WithCulture(CultureInfo culture);
        /// <summary>
        /// 构建服务引擎
        /// </summary>
        /// <returns></returns>
        IHost Build();
    }
}
