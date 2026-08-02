using Microsoft.Extensions.AI;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

namespace XiaoZhi.Net.Server.RAG.Abstractions
{
    /// <summary>
    /// Indexes a complete knowledge document
    /// </summary>
    public interface IKnowledgeAnalyzer
    {
        ValueTask<KnowledgeIndexingResult> IndexAsync(IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator, KnowledgeDocument document, CancellationToken cancellationToken = default);
    }
}
