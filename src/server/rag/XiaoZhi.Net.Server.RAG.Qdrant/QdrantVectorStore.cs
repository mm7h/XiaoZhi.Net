using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using XiaoZhi.Net.Server.RAG.Abstractions;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;
using static Qdrant.Client.Grpc.Conditions;

namespace XiaoZhi.Net.Server.RAG.Qdrant;

/// <summary>
/// Qdrant-backed vector store. The client is safe to share as a singleton.
/// </summary>
internal sealed class QdrantVectorStore : IVectorStore, IDisposable
{
    private const string KnowledgeBaseIdField = "knowledge_base_id";
    private const string SourceIdField = "source_id";
    private const string SourceNameField = "source_name";
    private const string ContentHashField = "content_hash";
    private const string ChunkIndexField = "chunk_index";
    private const string TextField = "text";
    private const string MetadataPrefix = "metadata.";

    private readonly QdrantClient _client;
    private readonly QdrantVectorStoreConfig _config;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private int _dimensions;

    public QdrantVectorStore(QdrantVectorStoreConfig config)
    {
        this._config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.CollectionName);
        this._client = new QdrantClient(config.Host, config.Port, config.UseTls, config.ApiKey);
    }

    public async ValueTask EnsureInitializedAsync(VectorStoreSchema schema, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schema.Dimensions);
        int existingDimensions = Interlocked.CompareExchange(ref this._dimensions, schema.Dimensions, 0);
        if (existingDimensions != 0 && existingDimensions != schema.Dimensions)
        {
            throw new InvalidOperationException($"The Qdrant vector store was initialized with {existingDimensions} dimensions, not {schema.Dimensions}.");
        }

        await this._initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (await this._client.CollectionExistsAsync(this._config.CollectionName, cancellationToken))
            {
                return;
            }

            await this._client.CreateCollectionAsync(this._config.CollectionName, new VectorParams
            {
                Size = (ulong)schema.Dimensions,
                Distance = Distance.Cosine
            }, cancellationToken: cancellationToken);
            await this._client.CreatePayloadIndexAsync(this._config.CollectionName, KnowledgeBaseIdField, PayloadSchemaType.Keyword, cancellationToken: cancellationToken);
            await this._client.CreatePayloadIndexAsync(this._config.CollectionName, SourceIdField, PayloadSchemaType.Keyword, cancellationToken: cancellationToken);
        }
        finally
        {
            this._initializationLock.Release();
        }
    }

    public async ValueTask<string?> GetSourceContentHashAsync(string knowledgeBaseId, string sourceId, CancellationToken cancellationToken = default)
    {
        if (!await this._client.CollectionExistsAsync(this._config.CollectionName, cancellationToken))
        {
            return null;
        }

        ScrollResponse response = await this._client.ScrollAsync(
            this._config.CollectionName,
            CreateSourceFilter(knowledgeBaseId, sourceId),
            limit: 1,
            payloadSelector: true,
            vectorsSelector: false,
            cancellationToken: cancellationToken);

        return response.Result.FirstOrDefault()?.Payload.TryGetValue(ContentHashField, out Value? hash) == true ? hash.StringValue : null;
    }

    public async ValueTask UpsertAsync(IReadOnlyCollection<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        if (this._dimensions == 0)
        {
            throw new InvalidOperationException("The vector store has not been initialized.");
        }

        List<PointStruct> points = records.Select(this.ToPoint).ToList();
        await this._client.UpsertAsync(this._config.CollectionName, points, cancellationToken: cancellationToken);
    }

    public async ValueTask DeleteAsync(VectorDeleteRequest request, CancellationToken cancellationToken = default)
    {
        Filter filter = CreateSourceFilter(request.KnowledgeBaseId, request.SourceId);
        if (!string.IsNullOrWhiteSpace(request.ExcludeContentHash))
        {
            filter.MustNot.Add(MatchKeyword(ContentHashField, request.ExcludeContentHash));
        }

        await this._client.DeleteAsync(this._config.CollectionName, filter, cancellationToken: cancellationToken);
    }

    public async ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (this._dimensions == 0)
        {
            return [];
        }

        if (request.Vector.Length != this._dimensions)
        {
            throw new InvalidOperationException($"Expected {this._dimensions} vector dimensions, but received {request.Vector.Length}.");
        }

        Filter filter = new();
        filter.Must.Add(MatchKeyword(KnowledgeBaseIdField, request.KnowledgeBaseId));
        if (request.MetadataFilter is not null)
        {
            foreach ((string key, string value) in request.MetadataFilter)
            {
                filter.Must.Add(MatchKeyword(MetadataPrefix + key, value));
            }
        }

        IReadOnlyList<ScoredPoint> points = await this._client.SearchAsync(
            this._config.CollectionName,
            request.Vector,
            filter,
            limit: (ulong)request.Limit,
            payloadSelector: true,
            scoreThreshold: request.MinimumScore,
            cancellationToken: cancellationToken);

        return points.Select(ToSearchHit).ToArray();
    }

    private PointStruct ToPoint(VectorRecord record)
    {
        if (record.Vector.Length != this._dimensions)
        {
            throw new InvalidOperationException($"Expected {this._dimensions} vector dimensions, but received {record.Vector.Length}.");
        }

        Dictionary<string, Value> payload = new()
        {
            [KnowledgeBaseIdField] = record.Chunk.KnowledgeBaseId,
            [SourceIdField] = record.Chunk.SourceId,
            [SourceNameField] = record.Chunk.SourceName,
            [ContentHashField] = record.Chunk.ContentHash,
            [ChunkIndexField] = record.Chunk.Index,
            [TextField] = record.Chunk.Text
        };
        if (record.Chunk.Metadata is not null)
        {
            foreach ((string key, string value) in record.Chunk.Metadata)
            {
                payload[MetadataPrefix + key] = value;
            }
        }

        return new PointStruct
        {
            Id = CreatePointId(record.Chunk.Id),
            Vectors = record.Vector.ToArray(),
            Payload = { payload }
        };
    }

    private static VectorSearchHit ToSearchHit(ScoredPoint point)
    {
        Dictionary<string, string> metadata = point.Payload
            .Where(pair => pair.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal) && pair.Value.KindCase == Value.KindOneofCase.StringValue)
            .ToDictionary(pair => pair.Key[MetadataPrefix.Length..], pair => pair.Value.StringValue, StringComparer.Ordinal);
        KnowledgeChunk chunk = new(
            point.Id.Uuid,
            GetPayloadString(point.Payload, KnowledgeBaseIdField),
            GetPayloadString(point.Payload, SourceIdField),
            GetPayloadString(point.Payload, SourceNameField),
            (int)GetPayloadInteger(point.Payload, ChunkIndexField),
            GetPayloadString(point.Payload, TextField),
            GetPayloadString(point.Payload, ContentHashField),
            metadata);
        return new VectorSearchHit(new VectorRecord(chunk, ReadOnlyMemory<float>.Empty), point.Score);
    }

    private static Filter CreateSourceFilter(string knowledgeBaseId, string sourceId)
    {
        Filter filter = new();
        filter.Must.Add(MatchKeyword(KnowledgeBaseIdField, knowledgeBaseId));
        filter.Must.Add(MatchKeyword(SourceIdField, sourceId));
        return filter;
    }

    private static Guid CreatePointId(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes[..16]);
    }

    private static string GetPayloadString(IReadOnlyDictionary<string, Value> payload, string key)
    {
        return payload.TryGetValue(key, out Value? value) ? value.StringValue : string.Empty;
    }

    private static long GetPayloadInteger(IReadOnlyDictionary<string, Value> payload, string key)
    {
        return payload.TryGetValue(key, out Value? value) ? value.IntegerValue : 0;
    }

    public void Dispose()
    {
        this._initializationLock.Dispose();
        this._client.Dispose();
    }
}
