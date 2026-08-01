using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Embeddings;
using System.ClientModel;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample20_RAG
    {
        private const string EndPoint = "https://open.bigmodel.cn/api/paas/v4/";
        private const string DefaultDashScopeEmbeddingEndpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/";
        private const string EmbeddingModel = "qwen3.7-text-embedding";
        private const string ChatModel = "glm-4.7-flash";

        private const string DocumentsDirectory = "doc";
        private const int ChunkSize = 800;
        private const int ChunkOverlap = 100;
        private const int EmbeddingBatchSize = 10;
        private const int SearchResultLimit = 5;

        public static async Task RunAsync()
        {
            using CancellationTokenSource cancellationSource = new();
            string operation = "初始化";
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellationSource.Cancel();
            };

            try
            {
                string chatApiKey = ReadRequiredEnvironmentVariable("OPEN_AI_API_KEY");
                string embeddingApiKey = ReadRequiredEnvironmentVariable("DASHSCOPE_API_KEY");
                Uri embeddingEndpoint = ReadDashScopeEmbeddingEndpoint();

                string documentDirectory = Path.Combine(AppContext.BaseDirectory, DocumentsDirectory);
                IReadOnlyList<RagDocumentChunk> chunks = LoadDocumentChunks(documentDirectory);

                if (chunks.Count == 0)
                {
                    Console.WriteLine($"未在 '{documentDirectory}' 找到可导入的 .md 或 .txt 文档。");
                    return;
                }

                OpenAIClient openAIClient = new(
                    new ApiKeyCredential(chatApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(EndPoint) });

                OpenAIClient embeddingOpenAIClient = new(
                    new ApiKeyCredential(embeddingApiKey),
                    new OpenAIClientOptions { Endpoint = embeddingEndpoint });

                operation = $"生成文档 embedding（模型：{EmbeddingModel}）";
                Console.WriteLine($"正在{operation}...");
                EmbeddingClient embeddingClient = embeddingOpenAIClient.GetEmbeddingClient(EmbeddingModel);
                InMemoryVectorStore vectorStore = await InMemoryVectorStore.CreateAsync(
                    chunks,
                    embeddingClient,
                    cancellationSource.Token);

                Console.WriteLine($"已建立内存索引：{vectorStore.Count} 个文本块，向量维度 {vectorStore.VectorDimension}。");
                Console.WriteLine("提示：索引仅存在于当前进程，重新启动后会重新生成 embedding。\n");

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

                IChatClient chatClient = openAIClient
                    .GetChatClient(ChatModel)
                    .AsIChatClient();

                ChatClientAgent agent = new(chatClient, new ChatClientAgentOptions
                {
                    Name = "Sample20_RAG",
                    Description = "A local in-memory RAG sample.",
                    ChatOptions = new ChatOptions
                    {
                        Instructions = "你是知识库问答助手。只可依据检索到的上下文回答；如果上下文不足，请明确回答“资料不足，无法确认”。回答末尾列出使用的文档来源。",
                        Temperature = 0.2f
                    },
                    AIContextProviders = [searchProvider],
                    ChatHistoryProvider = new InMemoryChatHistoryProvider(),
                    RequirePerServiceCallChatHistoryPersistence = false,
                    UseProvidedChatClientAsIs = false
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
                    operation = $"检索与调用聊天模型（embedding：{EmbeddingModel}，chat：{ChatModel}）";
                    AgentResponse response = await agent.RunAsync(question, session, cancellationToken: cancellationSource.Token);
                    PrintSearchResults(lastSearchResults);
                    Console.WriteLine($"\n回答：\n{response.Text}\n");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("操作已取消。");
            }
            catch (ClientResultException exception)
            {
                Console.WriteLine($"OpenAI 兼容服务调用失败（{operation}，HTTP {exception.Status}）：{exception.Message}");
            }
            catch (InvalidOperationException exception)
            {
                Console.WriteLine(exception.Message);
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
                string text = File.ReadAllText(filePath);
                int chunkIndex = 0;
                foreach (string chunkText in SplitText(text, ChunkSize, ChunkOverlap))
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
                int length = Math.Min(chunkSize, normalizedText.Length - start);
                string chunk = normalizedText.Substring(start, length).Trim();
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    yield return chunk;
                }
            }
        }

        private static bool IsExitCommand(string value) =>
            string.Equals(value.Trim(), "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "quit", StringComparison.OrdinalIgnoreCase);

        private static void PrintSearchResults(IReadOnlyList<SearchHit> results)
        {
            Console.WriteLine(results.Count == 0 ? "未检索到相关文档。" : "检索命中：");
            foreach (SearchHit result in results)
            {
                Console.WriteLine($"- {result.Chunk.SourcePath} ({result.Score:F3})");
            }
        }

        private static string ReadRequiredEnvironmentVariable(string variableName)
        {
            string? value = Environment.GetEnvironmentVariable(variableName)
                ?? Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User);

            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"缺少 {variableName} 环境变量。");
        }

        private static Uri ReadDashScopeEmbeddingEndpoint()
        {
            string endpoint = Environment.GetEnvironmentVariable("DASHSCOPE_EMBEDDING_ENDPOINT")
                ?? Environment.GetEnvironmentVariable("DASHSCOPE_EMBEDDING_ENDPOINT", EnvironmentVariableTarget.User)
                ?? DefaultDashScopeEmbeddingEndpoint;

            return Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
                ? uri
                : throw new InvalidOperationException("DASHSCOPE_EMBEDDING_ENDPOINT 必须是有效的绝对 URL。");
        }

        private sealed record RagDocumentChunk(string Id, string SourcePath, string Text, float[] Embedding);

        private sealed record SearchHit(RagDocumentChunk Chunk, float Score);

        private sealed class InMemoryVectorStore
        {
            private readonly IReadOnlyList<RagDocumentChunk> _chunks;
            private readonly EmbeddingClient _embeddingClient;

            private InMemoryVectorStore(IReadOnlyList<RagDocumentChunk> chunks, EmbeddingClient embeddingClient, int vectorDimension)
            {
                this._chunks = chunks;
                this._embeddingClient = embeddingClient;
                this.VectorDimension = vectorDimension;
            }

            public int Count => this._chunks.Count;

            public int VectorDimension { get; }

            public static async Task<InMemoryVectorStore> CreateAsync(
                IReadOnlyList<RagDocumentChunk> chunks,
                EmbeddingClient embeddingClient,
                CancellationToken cancellationToken)
            {
                List<RagDocumentChunk> embeddedChunks = [];
                int? vectorDimension = null;

                foreach (RagDocumentChunk[] batch in chunks.Chunk(EmbeddingBatchSize))
                {
                    OpenAIEmbeddingCollection embeddings = await embeddingClient.GenerateEmbeddingsAsync(
                        batch.Select(chunk => chunk.Text),
                        cancellationToken: cancellationToken);

                    if (embeddings.Count != batch.Length)
                    {
                        throw new InvalidOperationException("Embedding 服务返回的向量数量与输入文本数量不一致。");
                    }

                    for (int index = 0; index < batch.Length; index++)
                    {
                        float[] vector = embeddings[index].ToFloats().ToArray();
                        vectorDimension ??= vector.Length;
                        if (vector.Length != vectorDimension.Value)
                        {
                            throw new InvalidOperationException("文档 embedding 维度不一致，请检查 embedding 模型配置。");
                        }

                        embeddedChunks.Add(batch[index] with { Embedding = vector });
                    }
                }

                return new InMemoryVectorStore(embeddedChunks, embeddingClient, vectorDimension ?? 0);
            }

            public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
            {
                OpenAIEmbedding embedding = await this._embeddingClient.GenerateEmbeddingAsync(query, cancellationToken: cancellationToken);
                float[] queryVector = embedding.ToFloats().ToArray();
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
