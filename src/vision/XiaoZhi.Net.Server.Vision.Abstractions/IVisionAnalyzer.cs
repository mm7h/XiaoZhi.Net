using XiaoZhi.Net.Server.Vision.Abstractions.Common;

namespace XiaoZhi.Net.Server.Vision.Abstractions;

/// <summary>
/// 执行一次无状态的视觉语言模型推理。
/// </summary>
public interface IVisionAnalyzer
{
    Task<string> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken);
}
