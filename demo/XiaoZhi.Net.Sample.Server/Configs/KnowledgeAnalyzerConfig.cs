namespace XiaoZhi.Net.Server.RAG.Abstractions.Common.Configs
{
    public record KnowledgeAnalyzerConfig
    {
        public int ChunkSize { get; init; } = 512;
        public int ChunkOverlap { get; init; } = 64;
        public int EmbeddingBatchSize { get; init; } = 16;
    }
}
