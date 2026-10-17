namespace XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

public sealed record KnowledgeImportResult(
    string FilePath,
    KnowledgeImportStatus Status,
    int ChunkCount = 0,
    string? Error = null)
{
    public static KnowledgeImportResult Succeeded(string filePath, int chunkCount, bool wasSkipped) => new(
        filePath,
        wasSkipped ? KnowledgeImportStatus.Skipped : KnowledgeImportStatus.Succeeded,
        chunkCount);

    public static KnowledgeImportResult Unsupported(string filePath) => new(filePath, KnowledgeImportStatus.Unsupported);

    public static KnowledgeImportResult Failed(string filePath, string error) => new(filePath, KnowledgeImportStatus.Failed, Error: error);
}

public enum KnowledgeImportStatus
{
    Succeeded,
    Skipped,
    Unsupported,
    Failed
}
