using XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

namespace XiaoZhi.Net.Server.RAG.Abstractions;

/// <summary>Persistent or in-memory storage for embedding vectors and their retrievable payload.</summary>
public interface IVectorStore
{
    ValueTask EnsureInitializedAsync(VectorStoreSchema schema, CancellationToken cancellationToken = default);

    ValueTask<string?> GetSourceContentHashAsync(
        string knowledgeBaseId,
        string sourceId,
        CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(IReadOnlyCollection<VectorRecord> records, CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(VectorDeleteRequest request, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default);
}
