namespace XiaoZhi.Net.Server.Abstractions.Mcp;

/// <summary>
/// 为设备 MCP 初始化消息提供可选能力。
/// </summary>
/// <remarks>
/// 实现由可选模块提供，使核心服务无需了解具体能力的实现细节。
/// </remarks>
public interface IDeviceMcpCapabilityContributor
{
    IDeviceMcpCapabilityLease? CreateCapability(DeviceMcpCapabilityContext context);
}
