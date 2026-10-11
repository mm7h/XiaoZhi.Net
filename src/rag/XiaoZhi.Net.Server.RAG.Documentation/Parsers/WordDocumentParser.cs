using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using XiaoZhi.Net.Server.RAG.Documentation.Models;

namespace XiaoZhi.Net.Server.RAG.Documentation.Parsers;

internal sealed class WordDocumentParser : IDocumentParser
{
    public bool CanParse(string filePath) => string.Equals(Path.GetExtension(filePath), ".docx", StringComparison.OrdinalIgnoreCase);

    public Task<ParsedDocument> ParseAsync(DocumentParseRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using WordprocessingDocument document = WordprocessingDocument.Open(request.FilePath, false);
        Body? body = document.MainDocumentPart?.Document?.Body;
        string text = body is null
            ? string.Empty
            : string.Join("\n\n", body.Descendants<Paragraph>().Select(paragraph => paragraph.InnerText.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)));
        return Task.FromResult(new ParsedDocument(request.KnowledgeBaseId, request.SourceId, Path.GetFileName(request.FilePath), text, new Dictionary<string, string> { ["format"] = "docx" }));
    }
}
