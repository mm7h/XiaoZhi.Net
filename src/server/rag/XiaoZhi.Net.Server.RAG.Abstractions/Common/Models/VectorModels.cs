namespace XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

public sealed record VectorStoreSchema(int Dimensions, string Distance = "Cosine");

public sealed record VectorRecord(KnowledgeChunk Chunk, ReadOnlyMemory<float> Vector);

public sealed record VectorSearchRequest(
    string KnowledgeBaseId,
    ReadOnlyMemory<float> Vector,
    int Limit = 5,
    float? MinimumScore = null,
    IReadOnlyDictionary<string, string>? MetadataFilter = null);

public sealed record VectorSearchHit(VectorRecord Record, float Score);

public sealed record VectorDeleteRequest(
    string KnowledgeBaseId,
    string SourceId,
    string? ExcludeContentHash = null);
