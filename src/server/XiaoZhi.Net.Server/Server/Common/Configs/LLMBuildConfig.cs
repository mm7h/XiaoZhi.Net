using Microsoft.SemanticKernel;

namespace XiaoZhi.Net.Server.Common.Configs
{
    internal record LLMBuildConfig(
        string EmotionLLMModelName,
        string ChatLLMModelName,
        string Prompt,
        bool UseStreaming,
        bool UseEmotions,
        string SummaryMemory,
        Kernel Kernel);
}
