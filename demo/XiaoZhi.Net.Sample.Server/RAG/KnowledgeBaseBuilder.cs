using System.ClientModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using XiaoZhi.Net.Server;
using XiaoZhi.Net.Server.RAG.Abstractions;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Configs;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;

namespace XiaoZhi.Net.Sample.Server.RAG
{
    internal static class KnowledgeBaseBuilder
    {
        private static readonly HashSet<string> s_supportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".markdown" };

        static KnowledgeBaseBuilder()
        {
            VectorStore = new InMemoryVectorStore();
        }

        public static IVectorStore VectorStore { get; }

        public static IHost BuildKnowledgeBase(this IHost host, XiaoZhiConfig xiaoZhiConfig, string documentDirectory, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(documentDirectory))
            {
                throw new InvalidOperationException("RAG SourceDirectory is required when StoreType is Memory.");
            }

            string rootDirectory = Path.GetFullPath(documentDirectory);
            if (!Directory.Exists(rootDirectory))
            {
                throw new DirectoryNotFoundException($"The RAG source directory does not exist: {rootDirectory}");
            }


            var embeddingGenerator = BuildEmbeddingGenerator(xiaoZhiConfig);
            var knowledgeAnalyzer = BuildKnowledgeAnalyzer();

            BuildSampleKnowledgeBaseAsync(rootDirectory, knowledgeAnalyzer, "sample-knowledge-base", embeddingGenerator).GetAwaiter().GetResult();
            //BuildKnowledgeBaseFromDocumentsAsync(host, rootDirectory, "sample-knowledge-base", embeddingGenerator).GetAwaiter().GetResult();

            return host;
        }

        private static IEmbeddingGenerator<string, Embedding<float>> BuildEmbeddingGenerator(XiaoZhiConfig xiaoZhiConfig)
        {
            string? selectedEmbeddingModel = xiaoZhiConfig.SelectedSettings.GetValueOrDefault("RAG");

            if (string.IsNullOrWhiteSpace(selectedEmbeddingModel))
            {
                throw new ArgumentNullException(nameof(selectedEmbeddingModel), "RAG embedding model is required.");
            }

            var configuredEmbeddingModel = xiaoZhiConfig.ConfiguredSettings["LLM"].GetValueOrDefault(selectedEmbeddingModel);

            string? baseUrl = configuredEmbeddingModel?.GetValueOrDefault("BaseUrl");
            string? apiKey = configuredEmbeddingModel?.GetValueOrDefault("ApiKey");
            string? modelName = configuredEmbeddingModel?.GetValueOrDefault("ModelName");

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(modelName))
            {
                throw new InvalidOperationException("RAG embedding model configuration is invalid.");
            }

            OpenAIClient openAIClient = new OpenAIClient(
                        new ApiKeyCredential(apiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
            return openAIClient.GetEmbeddingClient(modelName).AsIEmbeddingGenerator();

        }

        private static IKnowledgeAnalyzer BuildKnowledgeAnalyzer()
        {
            KnowledgeAnalyzerConfig knowledgeAnalyzerConfig = new KnowledgeAnalyzerConfig();
            IKnowledgeAnalyzer knowledgeAnalyzer = new KnowledgeAnalyzer(knowledgeAnalyzerConfig, VectorStore);

            return knowledgeAnalyzer;

        }

        private static async Task BuildSampleKnowledgeBaseAsync(string rootDirectory, IKnowledgeAnalyzer knowledgeAnalyzer, string knowledgeBaseId, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
        {
            string[] sourceFiles = Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
                .Where(path => s_supportedExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (sourceFiles.Length == 0)
            {
                throw new InvalidOperationException($"No TXT or Markdown files were found in {rootDirectory}.");
            }


            int indexedSources = 0;
            foreach (string filePath in sourceFiles)
            {
                string text = File.ReadAllText(filePath).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Skipping empty RAG source {FilePath}.", filePath);
                    continue;
                }

                string sourceId = Path.GetRelativePath(rootDirectory, filePath).Replace(Path.DirectorySeparatorChar, '/');
                string contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                KnowledgeDocument document = new KnowledgeDocument(
                    knowledgeBaseId,
                    sourceId,
                    Path.GetFileName(filePath),
                    text,
                    contentHash,
                    new Dictionary<string, string> { ["format"] = Path.GetExtension(filePath).TrimStart('.') });
                await knowledgeAnalyzer.IndexAsync(embeddingGenerator, document);
                indexedSources++;
            }

            if (indexedSources == 0)
            {
                throw new InvalidOperationException($"No non-empty RAG source files were found in {rootDirectory}.");
            }
        }

        private static async Task BuildKnowledgeBaseFromDocumentsAsync(IHost host, string rootDirectory, string knowledgeBaseId, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
        {
            IKnowledgeImportPipeline knowledgeImportPipeline = host.Services.GetRequiredService<IKnowledgeImportPipeline>();

            if (!Directory.Exists(rootDirectory))
            {
                throw new FileNotFoundException($"No documnets in path {rootDirectory}.");
            }

            IReadOnlyList<KnowledgeImportResult> results = await knowledgeImportPipeline.ImportDirectoryAsync(embeddingGenerator, knowledgeBaseId, rootDirectory).ConfigureAwait(false);

            foreach (KnowledgeImportResult result in results)
            {
                Console.WriteLine($"{result.Status,-11} {result.ChunkCount,5} {result.FilePath}{(result.Error is null ? string.Empty : $" - {result.Error}")}");
            }

            int succeeded = results.Count(result => result.Status == KnowledgeImportStatus.Succeeded);
            int skipped = results.Count(result => result.Status == KnowledgeImportStatus.Skipped);
            int failed = results.Count(result => result.Status == KnowledgeImportStatus.Failed);
            Console.WriteLine($"Completed: {succeeded} indexed, {skipped} skipped, {failed} failed.");
        }
    }
}
