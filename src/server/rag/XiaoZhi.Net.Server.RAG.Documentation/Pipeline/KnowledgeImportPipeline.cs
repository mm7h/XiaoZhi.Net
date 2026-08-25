using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Server.RAG.Abstractions;
using XiaoZhi.Net.Server.RAG.Abstractions.Common.Models;
using XiaoZhi.Net.Server.RAG.Documentation.Models;
using XiaoZhi.Net.Server.RAG.Documentation.Parsers;

namespace XiaoZhi.Net.Server.RAG.Documentation.Pipeline;

/// <summary>
/// Parses a source file, normalizes and chunks it, then indexes it.
/// </summary>
internal sealed class KnowledgeImportPipeline : IKnowledgeImportPipeline
{
    private readonly IEnumerable<IDocumentParser> _documentParsers;

    public KnowledgeImportPipeline(IEnumerable<IDocumentParser> parsers, IKnowledgeAnalyzer knowledgeAnalyzer)
    {
        this._documentParsers = parsers?.ToArray() ?? throw new ArgumentNullException(nameof(parsers));
        this.KnowledgeAnalyzer = knowledgeAnalyzer ?? throw new ArgumentNullException(nameof(knowledgeAnalyzer));
    }

    public IKnowledgeAnalyzer KnowledgeAnalyzer { get; }

    public async Task<KnowledgeImportResult> ImportFileAsync(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, string knowledgeBaseId, string rootDirectory, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            string fullPath = Path.GetFullPath(filePath);
            IDocumentParser? parser = this._documentParsers.FirstOrDefault(item => item.CanParse(fullPath));
            if (parser is null)
            {
                return KnowledgeImportResult.Unsupported(fullPath);
            }

            string sourceId = Path.GetRelativePath(rootDirectory, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            ParsedDocument parsed = await parser.ParseAsync(new DocumentParseRequest(knowledgeBaseId, sourceId, fullPath), cancellationToken);
            string text = NormalizeText(parsed.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                return KnowledgeImportResult.Failed(fullPath, "The document does not contain extractable text.");
            }

            string contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            KnowledgeDocument document = new KnowledgeDocument(parsed.KnowledgeBaseId, parsed.SourceId, parsed.SourceName, text, contentHash, parsed.Metadata);
            KnowledgeIndexingResult result = await this.KnowledgeAnalyzer.IndexAsync(embeddingGenerator, document, cancellationToken);
            return KnowledgeImportResult.Succeeded(fullPath, result.ChunkCount, result.WasSkipped);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return KnowledgeImportResult.Failed(filePath, exception.Message);
        }
    }

    public async Task<IReadOnlyList<KnowledgeImportResult>> ImportDirectoryAsync(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, string knowledgeBaseId, string directoryPath, CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The knowledge source directory does not exist: {root}");
        }

        List<KnowledgeImportResult> results = [];
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await this.ImportFileAsync(embeddingGenerator, knowledgeBaseId, root, path, cancellationToken));
        }

        return results;
    }

    private static string NormalizeText(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }
}
