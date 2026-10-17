namespace XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

public sealed record RagRequest(
    string Query,
    string KnowledgeBaseId,
    int Limit = 5,
    float? MinimumScore = null,
    IReadOnlyDictionary<string, string>? MetadataFilter = null);

public sealed record RagContext(IReadOnlyList<VectorSearchHit> Hits)
{
    public bool HasMatches => this.Hits.Count > 0;
}

public sealed record KnowledgeIndexingResult(
    string KnowledgeBaseId,
    string SourceId,
    int ChunkCount,
    bool WasSkipped);
