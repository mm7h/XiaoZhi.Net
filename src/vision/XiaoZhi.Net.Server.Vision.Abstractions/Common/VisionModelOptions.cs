namespace XiaoZhi.Net.Server.Vision.Abstractions.Common;

/// <summary>
/// OpenAI 兼容视觉语言模型的连接和响应配置。
/// </summary>
public sealed class VisionModelOptions
{
    /// <summary>
    /// OpenAI 兼容 API 的服务地址。
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// 调用上游模型使用的访问密钥。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 上游视觉模型名称。
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 附加到设备问题后的模型响应要求。
    /// </summary>
    public string ResponseInstruction { get; set; } = "请使用中文简洁描述图片内容。";
}
