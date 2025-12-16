using Microsoft.SemanticKernel;

namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class LLMBuildConfig
    {
        public LLMBuildConfig(string emotionLLMModelName, string chatLLMModelName, string prompt, bool useStreaming, string summaryMemory, Kernel kernel)
        {
            this.EmotionLLMModelName = emotionLLMModelName;
            this.ChatLLMModelName = chatLLMModelName;
            this.Prompt = prompt;
            this.UseStreaming = useStreaming;
            this.SummaryMemory = summaryMemory;
            this.Kernel = kernel;
        }

        public string EmotionLLMModelName { get; }
        public string ChatLLMModelName { get; }
        public string Prompt { get; }
        public bool UseStreaming { get; }
        public string SummaryMemory { get; }
        public Kernel Kernel { get; }
    }
}
