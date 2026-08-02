using System.Collections.Concurrent;
using XiaoZhi.Net.Server.RAG.Abstractions;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

namespace XiaoZhi.Net.Sample.Server.RAG;

/// <summary>Thread-safe, non-persistent vector store intended for local development and tests.</summary>
internal sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, VectorRecord> _records = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string KnowledgeBaseId, string SourceId), string> _sourceHashes = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _dimensions;

    public ValueTask EnsureInitializedAsync(VectorStoreSchema schema, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schema.Dimensions);
        int dimensions = Interlocked.CompareExchange(ref this._dimensions, schema.Dimensions, 0);
        if (dimensions != 0 && dimensions != schema.Dimensions)
        {
            throw new InvalidOperationException($"The vector store was initialized with {dimensions} dimensions, not {schema.Dimensions}.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> GetSourceContentHashAsync(string knowledgeBaseId, string sourceId, CancellationToken cancellationToken = default)
    {
        this._sourceHashes.TryGetValue((knowledgeBaseId, sourceId), out string? hash);
        return ValueTask.FromResult(hash);
    }

    public async ValueTask UpsertAsync(IReadOnlyCollection<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        await this._writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (VectorRecord record in records)
            {
                this.ValidateVector(record.Vector);
                this._records[record.Chunk.Id] = record;
                this._sourceHashes[(record.Chunk.KnowledgeBaseId, record.Chunk.SourceId)] = record.Chunk.ContentHash;
            }
        }
        finally
        {
            this._writeLock.Release();
        }
    }

    public async ValueTask DeleteAsync(VectorDeleteRequest request, CancellationToken cancellationToken = default)
    {
        await this._writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach ((string id, VectorRecord record) in this._records)
            {
                if (!string.Equals(record.Chunk.KnowledgeBaseId, request.KnowledgeBaseId, StringComparison.Ordinal)
                    || !string.Equals(record.Chunk.SourceId, request.SourceId, StringComparison.Ordinal)
                    || request.ExcludeContentHash is not null && string.Equals(record.Chunk.ContentHash, request.ExcludeContentHash, StringComparison.Ordinal))
                {
                    continue;
                }

                this._records.TryRemove(id, out _);
            }

            if (request.ExcludeContentHash is null)
            {
                this._sourceHashes.TryRemove((request.KnowledgeBaseId, request.SourceId), out _);
            }
            else
            {
                this._sourceHashes[(request.KnowledgeBaseId, request.SourceId)] = request.ExcludeContentHash;
            }
        }
        finally
        {
            this._writeLock.Release();
        }
    }

    public ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        this.ValidateVector(request.Vector);
        IReadOnlyList<VectorSearchHit> hits = this._records.Values
            .Where(record => string.Equals(record.Chunk.KnowledgeBaseId, request.KnowledgeBaseId, StringComparison.Ordinal))
            .Where(record => MatchesMetadata(record.Chunk.Metadata, request.MetadataFilter))
            .Select(record => new VectorSearchHit(record, CosineSimilarity(request.Vector.Span, record.Vector.Span)))
            .Where(hit => request.MinimumScore is null || hit.Score >= request.MinimumScore.Value)
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Record.Chunk.Id, StringComparer.Ordinal)
            .Take(request.Limit)
            .ToArray();

        return ValueTask.FromResult(hits);
    }

    private void ValidateVector(ReadOnlyMemory<float> vector)
    {
        if (this._dimensions == 0)
        {
            throw new InvalidOperationException("The vector store has not been initialized.");
        }

        if (vector.Length != this._dimensions)
        {
            throw new InvalidOperationException($"Expected {this._dimensions} vector dimensions, but received {vector.Length}.");
        }
    }

    private static bool MatchesMetadata(IReadOnlyDictionary<string, string>? metadata, IReadOnlyDictionary<string, string>? filter)
    {
        if (filter is null || filter.Count == 0)
        {
            return true;
        }

        return metadata is not null && filter.All(pair => metadata.TryGetValue(pair.Key, out string? value) && string.Equals(value, pair.Value, StringComparison.Ordinal));
    }

    private static float CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (int index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        return leftMagnitude == 0 || rightMagnitude == 0 ? 0 : (float)(dot / Math.Sqrt(leftMagnitude * rightMagnitude));
    }
}
