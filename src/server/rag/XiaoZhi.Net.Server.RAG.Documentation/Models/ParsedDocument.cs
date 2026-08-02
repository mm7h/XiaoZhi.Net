namespace XiaoZhi.Net.Server.RAG.Documentation.Models
{
    internal sealed record ParsedDocument(
    string KnowledgeBaseId,
    string SourceId,
    string SourceName,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata = null);
}
