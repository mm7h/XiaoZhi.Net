// 此类已废弃：工具路由逻辑已内置到 AIFunction 闭包中（IoTClient/BaseMcpClient），无需再通过 Filter 路由。
// 保留此文件以防历史引用，实际已不注册到 DI。

namespace XiaoZhi.Net.Server.Providers.LLM.FunctionInvocationFilters
{
    /// <summary>已废弃。工具调用路由已通过 AIFunction 自路由闭包实现，此类不再使用。</summary>
    internal class MCPToolFunctionFilter
    {
    }
}
