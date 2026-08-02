using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Server.RAG.Abstractions;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Configs;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

namespace XiaoZhi.Net.Sample.Server.RAG;

internal sealed class KnowledgeAnalyzer : IKnowledgeAnalyzer
{

    private readonly int _chunkSize;
    private readonly int _chunkOverlap;
    private readonly int _embeddingBatchSize;

    private readonly IVectorStore _vectorStore;

    public KnowledgeAnalyzer(KnowledgeAnalyzerConfig config, IVectorStore vectorStore)
    {
        if (config.ChunkOverlap >= config.ChunkSize)
        {
            throw new ArgumentException("Chunk overlap must be less than chunk size.");
        }

        if (config.EmbeddingBatchSize <= 0)
        {
            throw new ArgumentException("Embedding batch size must be greater than zero.")  ;
        }

        this._chunkSize = config.ChunkSize;
        this._chunkOverlap = config.ChunkOverlap;
        this._embeddingBatchSize = config.EmbeddingBatchSize;

        this._vectorStore = vectorStore;
    }


    public ValueTask<KnowledgeIndexingResult> IndexAsync(IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator, KnowledgeDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return this.WriteAsync(embeddingGenerator, document, this.CreateChunks(document), cancellationToken);
    }

    private IReadOnlyList<KnowledgeChunk> CreateChunks(KnowledgeDocument document)
    {
        List<string> chunks = [];
        string normalized = document.Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        foreach (string paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (paragraph.Length <= this._chunkSize)
            {
                this.AppendParagraph(chunks, paragraph);
                continue;
            }

            for (int start = 0; start < paragraph.Length; start += this._chunkSize - this._chunkOverlap)
            {
                chunks.Add(paragraph.Substring(start, Math.Min(this._chunkSize, paragraph.Length - start)).Trim());
            }
        }

        return chunks.Where(text => !string.IsNullOrWhiteSpace(text)).Select((text, index) => new KnowledgeChunk(
            CreateChunkId(document.KnowledgeBaseId, document.SourceId, document.ContentHash, index),
            document.KnowledgeBaseId,
            document.SourceId,
            document.SourceName,
            index,
            text,
            document.ContentHash,
            document.Metadata)).ToArray();
    }

    private void AppendParagraph(List<string> chunks, string paragraph)
    {
        if (chunks.Count == 0)
        {
            chunks.Add(paragraph);
            return;
        }

        string current = chunks[^1];
        string combined = $"{current}\n\n{paragraph}";
        if (combined.Length <= this._chunkSize)
        {
            chunks[^1] = combined;
            return;
        }

        int availableOverlap = Math.Max(0, this._chunkSize - paragraph.Length - 2);
        int overlapLength = Math.Min(this._chunkOverlap, Math.Min(current.Length, availableOverlap));
        string overlap = overlapLength == 0 ? string.Empty : current[^overlapLength..];
        chunks.Add(string.IsNullOrEmpty(overlap) ? paragraph : $"{overlap}\n\n{paragraph}");
    }

    private async ValueTask<KnowledgeIndexingResult> WriteAsync(
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator,
        KnowledgeDocument document,
        IReadOnlyList<KnowledgeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document.KnowledgeBaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.ContentHash);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);

        string? existingHash = await this._vectorStore.GetSourceContentHashAsync(document.KnowledgeBaseId, document.SourceId, cancellationToken).ConfigureAwait(false);
        if (string.Equals(existingHash, document.ContentHash, StringComparison.Ordinal))
        {
            return new KnowledgeIndexingResult(document.KnowledgeBaseId, document.SourceId, 0, true);
        }

        if (chunks.Count == 0)
        {
            await this._vectorStore.DeleteAsync(new VectorDeleteRequest(document.KnowledgeBaseId, document.SourceId), cancellationToken).ConfigureAwait(false);
            return new KnowledgeIndexingResult(document.KnowledgeBaseId, document.SourceId, 0, false);
        }

        List<VectorRecord> records = new(chunks.Count);
        foreach (KnowledgeChunk[] batch in chunks.Chunk(this._embeddingBatchSize))
        {
            GeneratedEmbeddings<Embedding<float>> embeddings = await embeddingGenerator.GenerateAsync(
                batch.Select(chunk => chunk.Text), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (embeddings.Count != batch.Length)
            {
                throw new InvalidOperationException("The embedding generator returned a different number of embeddings than input chunks.");
            }

            for (int index = 0; index < batch.Length; index++)
            {
                records.Add(new VectorRecord(batch[index], embeddings[index].Vector));
            }
        }

        int dimensions = records[0].Vector.Length;
        if (records.Any(record => record.Vector.Length != dimensions))
        {
            throw new InvalidOperationException("The embedding generator returned vectors with inconsistent dimensions.");
        }

        await this._vectorStore.EnsureInitializedAsync(new VectorStoreSchema(dimensions), cancellationToken).ConfigureAwait(false);
        await this._vectorStore.UpsertAsync(records, cancellationToken).ConfigureAwait(false);
        await this._vectorStore.DeleteAsync(
            new VectorDeleteRequest(document.KnowledgeBaseId, document.SourceId, document.ContentHash), cancellationToken).ConfigureAwait(false);

        return new KnowledgeIndexingResult(document.KnowledgeBaseId, document.SourceId, records.Count, false);
    }

    private static string CreateChunkId(string knowledgeBaseId, string sourceId, string contentHash, int index)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{knowledgeBaseId}\n{sourceId}\n{contentHash}\n{index}"));
        return Convert.ToHexString(hash);
    }
}
