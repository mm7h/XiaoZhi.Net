namespace XiaoZhi.Net.Server.Vision.Abstractions.Common;

/// <summary>
/// 可选 Vision 模块的 HTTP 托管、设备鉴权与上游模型配置。
/// </summary>
public sealed class VisionServerOptions
{
    /// <summary>
    /// Kestrel 监听地址，例如 <c>http://0.0.0.0:8003</c>。
    /// </summary>
    public string ListenUrl { get; set; } = "http://0.0.0.0:8003";

    /// <summary>
    /// 设备可访问的完整图片说明接口地址，包含路径。
    /// </summary>
    public string PublicExplainUrl { get; set; } = string.Empty;

    /// <summary>
    /// Vision HTTP 接口的本地路由路径。
    /// </summary>
    public string ExplainPath { get; set; } = "/mcp/vision/explain";

    /// <summary>
    /// 仅用于签发会话范围设备上传令牌的签名密钥。
    /// </summary>
    public string UploadTokenSigningKey { get; set; } = string.Empty;

    /// <summary>
    /// 允许上传的最大图片字节数。
    /// </summary>
    public long MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// 单次视觉推理的最大耗时。
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 上游视觉语言模型的连接配置。
    /// </summary>
    public VisionModelOptions Model { get; set; } = new();
}
