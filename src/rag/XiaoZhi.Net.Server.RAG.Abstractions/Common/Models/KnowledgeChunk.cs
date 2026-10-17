namespace XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

/// <summary>A retrievable segment belonging to a knowledge document.</summary>
public sealed record KnowledgeChunk(
    string Id,
    string KnowledgeBaseId,
    string SourceId,
    string SourceName,
    int Index,
    string Text,
    string ContentHash,
    IReadOnlyDictionary<string, string>? Metadata = null);
