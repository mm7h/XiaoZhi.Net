using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.RAG.Abstractions;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

namespace XiaoZhi.Net.Server.Resources.Rag
{
    internal class DefaultRag : BaseResource<DefaultRag, ModelSetting>, IRag
    {
        private readonly IVectorStore _vectorStore;
        private readonly IServiceProvider _serviceProvider;

        private string _knowledgeBaseId = string.Empty;
        private int _topK;
        private float? _minimumScore;

        private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
        public DefaultRag(IServiceProvider serviceProvider,
            IVectorStore vectorStore,
            ILogger<DefaultRag> logger) : base(logger)
        {
            this._serviceProvider = serviceProvider;
            this._vectorStore = vectorStore;
        }

        public override string ResourceName => throw new NotImplementedException();

        public bool IsReady { get; private set; }

        public override bool Load(ModelSetting settings)
        {
            try
            {
                string? embeddingModelName = settings.Config.GetConfigValueOrDefault("EmbeddingLLM");

                if (string.IsNullOrWhiteSpace(embeddingModelName))
                {
                    this.Logger.LogError(Lang.DefaultRag_Load_EmbeddingModelMissing);
                    return false;
                }

                this._embeddingGenerator = this._serviceProvider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>($"RAG_LLM_{embeddingModelName}");

                string? documentDirectory = settings.Config.GetConfigValueOrDefault("DocumentDirectory");
                if (string.IsNullOrWhiteSpace(documentDirectory) || !Directory.Exists(documentDirectory))
                {
                    this.Logger.LogError(Lang.DefaultRag_Load_DocumentDirectoryInvalid, documentDirectory ?? string.Empty);
                    return false;
                }

                this._knowledgeBaseId = settings.Config.GetValueOrDefault("KnowledgeBaseId", "default");
                this._topK = settings.Config.GetConfigValueOrDefault("TopK", 5);
                this._minimumScore = settings.Config.GetConfigValueOrDefault<float?>("MinimumScore", null);

                this.IsReady = true;

                this.Logger.LogInformation(
                    Lang.DefaultRag_Load_Loaded,
                    this._knowledgeBaseId,
                    embeddingModelName,
                    this._topK,
                    this._minimumScore);
                return true;
            }
            catch (Exception ex)
            {
                this.IsReady = false;
                this.Logger.LogError(ex, Lang.DefaultRag_Load_LoadFailed);
                throw;
            }
        }
        public TextSearchProvider? Create()
        {
            if (!this.IsReady)
            {
                this.Logger.LogWarning(Lang.DefaultRag_Create_NotReady);
                return null;
            }

            return new TextSearchProvider(this.SearchAsync, new TextSearchProviderOptions
            {
                SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke
            });
        }

        private async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            this.Logger.LogDebug(Lang.DefaultRag_SearchAsync_QueryReceived, this._knowledgeBaseId, query.Length);
            RagContext context = await this.RetrieveAsync(
                    new RagRequest(query, this._knowledgeBaseId, this._topK, this._minimumScore), cancellationToken);

            return context.Hits.Select(hit => new TextSearchProvider.TextSearchResult
            {
                SourceName = hit.Record.Chunk.SourceName,
                SourceLink = hit.Record.Chunk.SourceId,
                Text = hit.Record.Chunk.Text,
                RawRepresentation = hit
            });
        }

        private async ValueTask<RagContext> RetrieveAsync(RagRequest request, CancellationToken cancellationToken = default)
        {
            if (this._embeddingGenerator is null)
            {
                this.Logger.LogError(Lang.DefaultRag_RetrieveAsync_EmbeddingGeneratorNotInitialized);
                throw new InvalidOperationException(Lang.DefaultRag_RetrieveAsync_EmbeddingGeneratorNotInitialized);
            }

            GeneratedEmbeddings<Embedding<float>> embeddings = await this._embeddingGenerator.GenerateAsync(
            [request.Query], cancellationToken: cancellationToken);
            if (embeddings.Count != 1)
            {
                this.Logger.LogError(Lang.DefaultRag_RetrieveAsync_EmbeddingCountInvalid, embeddings.Count);
                throw new InvalidOperationException("The embedding generator did not return a query embedding.");
            }

            IReadOnlyList<VectorSearchHit> hits = await this._vectorStore.SearchAsync(
                new VectorSearchRequest(
                    request.KnowledgeBaseId,
                    embeddings[0].Vector,
                    request.Limit,
                    request.MinimumScore,
                    request.MetadataFilter),
                cancellationToken);

            return new RagContext(hits);
        }

        public override void Dispose()
        {

        }
    }
}
