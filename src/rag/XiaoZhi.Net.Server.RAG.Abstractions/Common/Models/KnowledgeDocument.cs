namespace XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

/// <summary>Represents normalized textual content from one knowledge source.</summary>
public sealed record KnowledgeDocument(
    string KnowledgeBaseId,
    string SourceId,
    string SourceName,
    string Text,
    string ContentHash,
    IReadOnlyDictionary<string, string>? Metadata = null);
