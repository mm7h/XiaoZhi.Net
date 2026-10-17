namespace XiaoZhi.Net.Server.Abstractions.Mcp;

/// <summary>
/// 标识正在创建 MCP 能力的设备会话。
/// </summary>
public sealed record DeviceMcpCapabilityContext(string SessionId, string DeviceId);
