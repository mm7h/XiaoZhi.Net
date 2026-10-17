using System.Text;
using XiaoZhi.Net.Server.RAG.Documentation.Models;

namespace XiaoZhi.Net.Server.RAG.Documentation.Parsers;

internal sealed class PlainTextDocumentParser : IDocumentParser
{
    private static readonly HashSet<string> s_extensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".markdown" };

    public bool CanParse(string filePath) => s_extensions.Contains(Path.GetExtension(filePath));

    public async Task<ParsedDocument> ParseAsync(DocumentParseRequest request, CancellationToken cancellationToken = default)
    {
        string text = await File.ReadAllTextAsync(request.FilePath, Encoding.UTF8, cancellationToken);
        return new ParsedDocument(request.KnowledgeBaseId, request.SourceId, Path.GetFileName(request.FilePath), text);
    }
}
