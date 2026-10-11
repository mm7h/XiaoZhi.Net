using System.Text.Json.Nodes;
using XiaoZhi.Net.Server.Abstractions.Mcp;
using XiaoZhi.Net.Server.Vision.Abstractions.Common;
using XiaoZhi.Net.Server.Vision.Common;
using XiaoZhi.Net.Server.Vision.Common.Contexts;

namespace XiaoZhi.Net.Server.Vision.Services;

/// <summary>
/// 为已启用 Vision 的设备会话下发上传地址和独立令牌。
/// </summary>
internal sealed class VisionMcpCapabilityContributor : IDeviceMcpCapabilityContributor
{
    private readonly VisionServerOptions _options;
    private readonly VisionUploadTokenService _tokens;

    public VisionMcpCapabilityContributor(VisionServerOptions options, VisionUploadTokenService tokens)
    {
        this._options = options;
        this._tokens = tokens;
    }

    public IDeviceMcpCapabilityLease CreateCapability(DeviceMcpCapabilityContext context)
    {
        IssuedVisionUploadToken issued = this._tokens.Issue(context);
        JsonObject capability = new()
        {
            ["url"] = this._options.PublicExplainUrl,
            ["token"] = issued.Token
        };

        return new VisionMcpCapabilityLease("vision", capability, () => this._tokens.Revoke(issued.TokenId));
    }
}
