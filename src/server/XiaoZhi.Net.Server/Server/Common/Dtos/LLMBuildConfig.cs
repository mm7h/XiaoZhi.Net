using Microsoft.SemanticKernel;

namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class LLMBuildConfig
    {
        public LLMBuildConfig(string llmModelName, string prompt, bool useStreaming, string summaryMemory, Kernel kernel)
        {
            this.LlmModelName = llmModelName;
            this.Prompt = prompt;
            this.UseStreaming = useStreaming;
            this.SummaryMemory = summaryMemory;
            this.Kernel = kernel;
        }

        public string LlmModelName { get; }
        public string Prompt { get; }
        public bool UseStreaming { get; }
        public string SummaryMemory { get; }
        public Kernel Kernel { get; }
    }
}
