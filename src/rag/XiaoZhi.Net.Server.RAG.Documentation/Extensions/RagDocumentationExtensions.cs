using Microsoft.Extensions.DependencyInjection;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.RAG.Abstractions;
using XiaoZhi.Net.Server.RAG.Documentation.Parsers;
using XiaoZhi.Net.Server.RAG.Documentation.Pipeline;

namespace XiaoZhi.Net.Server.RAG.Documentation.Extensions;

public static class RagDocumentationExtensions
{
    public static IServerBuilder AddQdrantVectorStore(this IServerBuilder builder, IKnowledgeAnalyzer knowledgeAnalyzer)
    {
        builder.HostBuilder.ConfigureServices((context, services) =>
        {
            services.AddSingleton<IKnowledgeAnalyzer>(knowledgeAnalyzer);

            services.AddSingleton<IDocumentParser, PlainTextDocumentParser>();
            services.AddSingleton<IDocumentParser, PdfDocumentParser>();
            services.AddSingleton<IDocumentParser, WordDocumentParser>();
            services.AddSingleton<IDocumentParser, ExcelDocumentParser>();

            services.AddSingleton<IKnowledgeImportPipeline, KnowledgeImportPipeline>();
        });
        return builder;
    }
}
