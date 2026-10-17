using System.Text.Json.Nodes;

namespace XiaoZhi.Net.Server.Abstractions.Mcp;

/// <summary>
/// 持有一个会话范围的 MCP 能力，并在释放时回收关联资源。
/// </summary>
public interface IDeviceMcpCapabilityLease : IDisposable
{
    string CapabilityName { get; }

    JsonNode Capability { get; }
}
