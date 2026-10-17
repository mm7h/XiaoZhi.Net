using System.Data;
using System.Text;
using MiniExcelLibs;
using XiaoZhi.Net.Server.RAG.Documentation.Models;

namespace XiaoZhi.Net.Server.RAG.Documentation.Parsers;

internal sealed class ExcelDocumentParser : IDocumentParser
{
    private static readonly HashSet<string> s_extensions = new(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".csv" };

    public bool CanParse(string filePath) => s_extensions.Contains(Path.GetExtension(filePath));

    public Task<ParsedDocument> ParseAsync(DocumentParseRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StringBuilder text = new();
        if (string.Equals(Path.GetExtension(request.FilePath), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            AppendRows(text, request.FilePath, null, cancellationToken);
        }
        else
        {
            foreach (string sheetName in MiniExcel.GetSheetNames(request.FilePath))
            {
                text.AppendLine($"[Sheet: {sheetName}]");
                AppendRows(text, request.FilePath, sheetName, cancellationToken);
                text.AppendLine();
            }
        }

        return Task.FromResult(new ParsedDocument(request.KnowledgeBaseId, request.SourceId, Path.GetFileName(request.FilePath), text.ToString(), new Dictionary<string, string> { ["format"] = "excel" }));
    }

    private static void AppendRows(StringBuilder text, string filePath, string? sheetName, CancellationToken cancellationToken)
    {
        using IDataReader reader = MiniExcel.GetReader(filePath, useHeaderRow: false, sheetName: sheetName);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string row = string.Join(" | ", Enumerable.Range(0, reader.FieldCount)
                .Select(index => Convert.ToString(reader.GetValue(index))?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(row))
            {
                text.AppendLine(row);
            }
        }
    }
}
