using System.Text.Json.Nodes;
using XiaoZhi.Net.Server.Abstractions.Mcp;

namespace XiaoZhi.Net.Server.Vision.Common.Contexts;

/// <summary>
/// 管理 Vision MCP 能力在设备会话结束时的资源释放。
/// </summary>
internal sealed class VisionMcpCapabilityLease : IDeviceMcpCapabilityLease
{
    private Action? _release;

    public VisionMcpCapabilityLease(string capabilityName, JsonNode capability, Action release)
    {
        this.CapabilityName = capabilityName;
        this.Capability = capability;
        this._release = release;
    }

    public string CapabilityName { get; }

    public JsonNode Capability { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref this._release, null)?.Invoke();
    }
}
