using System.Text;
using UglyToad.PdfPig;
using XiaoZhi.Net.Server.RAG.Documentation.Models;

namespace XiaoZhi.Net.Server.RAG.Documentation.Parsers;

internal sealed class PdfDocumentParser : IDocumentParser
{
    public bool CanParse(string filePath) => string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ParsedDocument> ParseAsync(DocumentParseRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using PdfDocument document = PdfDocument.Open(request.FilePath);
        StringBuilder text = new();
        int pageNumber = 0;
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageNumber++;
            string pageText = page.Text.Trim();
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                text.AppendLine($"[Page {pageNumber}]");
                text.AppendLine(pageText);
                text.AppendLine();
            }
        }

        return Task.FromResult(new ParsedDocument(request.KnowledgeBaseId, request.SourceId, Path.GetFileName(request.FilePath), text.ToString(), new Dictionary<string, string> { ["format"] = "pdf" }));
    }
}
