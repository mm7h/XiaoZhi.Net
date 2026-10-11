using Microsoft.Extensions.AI;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

namespace XiaoZhi.Net.Server.RAG.Abstractions;

/// <summary>Imports source files into a knowledge base.</summary>
public interface IKnowledgeImportPipeline
{
    IKnowledgeAnalyzer KnowledgeAnalyzer { get; }
    Task<KnowledgeImportResult> ImportFileAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        string knowledgeBaseId,
        string rootDirectory,
        string filePath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeImportResult>> ImportDirectoryAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        string knowledgeBaseId,
        string directoryPath,
        CancellationToken cancellationToken = default);
}
