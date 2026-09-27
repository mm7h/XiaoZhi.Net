namespace XiaoZhi.Net.Server.Vision.Abstractions.Common;

/// <summary>
/// 描述一次视觉分析请求的文本和图片内容。
/// </summary>
public sealed record VisionAnalysisRequest(string Question, ReadOnlyMemory<byte> Image, string MediaType);
