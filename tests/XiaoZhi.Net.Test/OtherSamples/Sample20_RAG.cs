using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Test.Runtime;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample20_RAG
    {
        private const string DocumentsDirectory = "doc";
        private const int ChunkSize = 800;
        private const int ChunkOverlap = 100;
        private const int EmbeddingBatchSize = 10;
        private const int SearchResultLimit = 5;

        public static async Task RunAsync()
        {
            using CancellationTokenSource cancellationSource = new();
            using SampleAiRuntime runtime = SampleAiRuntime.Create();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellationSource.Cancel();
            };

            string documentDirectory = Path.Combine(AppContext.BaseDirectory, DocumentsDirectory);
            IReadOnlyList<RagDocumentChunk> chunks = LoadDocumentChunks(documentDirectory);
            if (chunks.Count == 0)
            {
                Console.WriteLine($"未在 '{documentDirectory}' 找到可导入的 .md 或 .txt 文档。");
                return;
            }

            Console.WriteLine("正在生成文档 embedding...");
            InMemoryVectorStore vectorStore = await InMemoryVectorStore.CreateAsync(chunks, runtime.EmbeddingGenerator, cancellationSource.Token);
            Console.WriteLine($"已建立内存索引：{vectorStore.Count} 个文本块，向量维度 {vectorStore.VectorDimension}。");

            IReadOnlyList<SearchHit> lastSearchResults = [];
            TextSearchProvider searchProvider = new(async (query, token) =>
            {
                lastSearchResults = await vectorStore.SearchAsync(query, SearchResultLimit, token);
                return lastSearchResults.Select(hit => new TextSearchProvider.TextSearchResult
                {
                    SourceName = hit.Chunk.SourcePath,
                    SourceLink = hit.Chunk.SourcePath,
                    Text = hit.Chunk.Text,
                    RawRepresentation = hit
                });
            }, new TextSearchProviderOptions
            {
                SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke
            });

            ChatClientAgent agent = new(runtime.ChatClient, new ChatClientAgentOptions
            {
                Name = nameof(Sample20_RAG),
                Description = "A local in-memory RAG sample.",
                ChatOptions = new ChatOptions
                {
                    Instructions = "你是知识库问答助手。只可依据检索到的上下文回答；如果上下文不足，请明确回答“资料不足，无法确认”。回答末尾列出使用的文档来源。",
                    Temperature = 0.2f
                },
                AIContextProviders = [searchProvider]
            });

            AgentSession session = await agent.CreateSessionAsync(cancellationToken: cancellationSource.Token);
            Console.WriteLine("请输入问题；输入 exit 或 quit 结束。\n");
            while (!cancellationSource.IsCancellationRequested)
            {
                Console.Write("> ");
                string? question = Console.ReadLine();
                if (question is null || IsExitCommand(question))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(question))
                {
                    continue;
                }

                lastSearchResults = [];
                AgentResponse response = await agent.RunAsync(question, session, cancellationToken: cancellationSource.Token);
                PrintSearchResults(lastSearchResults);
                Console.WriteLine($"\n回答：\n{response.Text}\n");
            }
        }

        private static IReadOnlyList<RagDocumentChunk> LoadDocumentChunks(string documentDirectory)
        {
            if (!Directory.Exists(documentDirectory))
            {
                return [];
            }

            List<RagDocumentChunk> chunks = [];
            foreach (string filePath in Directory.EnumerateFiles(documentDirectory, "*", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string sourcePath = Path.GetRelativePath(documentDirectory, filePath).Replace(Path.DirectorySeparatorChar, '/');
                int chunkIndex = 0;
                foreach (string chunkText in SplitText(File.ReadAllText(filePath), ChunkSize, ChunkOverlap))
                {
                    chunks.Add(new RagDocumentChunk($"{sourcePath}#{chunkIndex++}", sourcePath, chunkText, []));
                }
            }

            return chunks;
        }

        private static IEnumerable<string> SplitText(string text, int chunkSize, int overlap)
        {
            string normalizedText = text.Replace("\r\n", "\n").Trim();
            for (int start = 0; start < normalizedText.Length; start += chunkSize - overlap)
            {
                string chunk = normalizedText.Substring(start, Math.Min(chunkSize, normalizedText.Length - start)).Trim();
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    yield return chunk;
                }
            }
        }

        private static bool IsExitCommand(string value) => string.Equals(value.Trim(), "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "quit", StringComparison.OrdinalIgnoreCase);

        private static void PrintSearchResults(IReadOnlyList<SearchHit> results)
        {
            Console.WriteLine(results.Count == 0 ? "未检索到相关文档。" : "检索命中：");
            foreach (SearchHit result in results)
            {
                Console.WriteLine($"- {result.Chunk.SourcePath} ({result.Score:F3})");
            }
        }

        private sealed record RagDocumentChunk(string Id, string SourcePath, string Text, float[] Embedding);

        private sealed record SearchHit(RagDocumentChunk Chunk, float Score);

        private sealed class InMemoryVectorStore
        {
            private readonly IReadOnlyList<RagDocumentChunk> _chunks;
            private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

            private InMemoryVectorStore(IReadOnlyList<RagDocumentChunk> chunks, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, int vectorDimension)
            {
                this._chunks = chunks;
                this._embeddingGenerator = embeddingGenerator;
                this.VectorDimension = vectorDimension;
            }

            public int Count => this._chunks.Count;

            public int VectorDimension { get; }

            public static async Task<InMemoryVectorStore> CreateAsync(IReadOnlyList<RagDocumentChunk> chunks, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, CancellationToken cancellationToken)
            {
                List<RagDocumentChunk> embeddedChunks = [];
                int? vectorDimension = null;
                foreach (RagDocumentChunk[] batch in chunks.Chunk(EmbeddingBatchSize))
                {
                    GeneratedEmbeddings<Embedding<float>> embeddings = await embeddingGenerator.GenerateAsync(batch.Select(chunk => chunk.Text), cancellationToken: cancellationToken);
                    if (embeddings.Count != batch.Length)
                    {
                        throw new InvalidOperationException("Embedding 服务返回的向量数量与输入文本数量不一致。");
                    }

                    for (int index = 0; index < batch.Length; index++)
                    {
                        float[] vector = embeddings[index].Vector.ToArray();
                        vectorDimension ??= vector.Length;
                        if (vector.Length != vectorDimension.Value)
                        {
                            throw new InvalidOperationException("文档 embedding 维度不一致，请检查 embedding 模型配置。");
                        }

                        embeddedChunks.Add(batch[index] with { Embedding = vector });
                    }
                }

                return new InMemoryVectorStore(embeddedChunks, embeddingGenerator, vectorDimension ?? 0);
            }

            public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
            {
                Embedding<float> embedding = await this._embeddingGenerator.GenerateAsync(query, cancellationToken: cancellationToken);
                float[] queryVector = embedding.Vector.ToArray();
                if (queryVector.Length != this.VectorDimension)
                {
                    throw new InvalidOperationException("查询 embedding 维度与文档索引不一致，请使用同一 embedding 模型重建索引。");
                }

                return this._chunks
                    .Select(chunk => new SearchHit(chunk, CosineSimilarity(queryVector, chunk.Embedding)))
                    .OrderByDescending(hit => hit.Score)
                    .ThenBy(hit => hit.Chunk.Id, StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray();
            }

            private static float CosineSimilarity(float[] left, float[] right)
            {
                double dotProduct = 0;
                double leftMagnitude = 0;
                double rightMagnitude = 0;
                for (int index = 0; index < left.Length; index++)
                {
                    dotProduct += left[index] * right[index];
                    leftMagnitude += left[index] * left[index];
                    rightMagnitude += right[index] * right[index];
                }

                return leftMagnitude == 0 || rightMagnitude == 0
                    ? 0
                    : (float)(dotProduct / Math.Sqrt(leftMagnitude * rightMagnitude));
            }
        }
    }
}
