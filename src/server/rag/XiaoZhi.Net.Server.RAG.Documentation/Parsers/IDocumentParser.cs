using XiaoZhi.Net.Server.RAG.Documentation.Models;

namespace XiaoZhi.Net.Server.RAG.Documentation.Parsers;

internal interface IDocumentParser
{
    bool CanParse(string filePath);

    Task<ParsedDocument> ParseAsync(DocumentParseRequest request, CancellationToken cancellationToken = default);
}
