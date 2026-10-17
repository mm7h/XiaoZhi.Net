using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.RAG.Qdrant;

namespace XiaoZhi.Net.Server.RAG.Abstractions;

public static class QdrantRagExtensions
{
    public static IServerBuilder AddQdrantVectorStore(this IServerBuilder builder, QdrantVectorStoreConfig config)
    {
        builder.HostBuilder.ConfigureServices((context, services) =>
        {
            services.Replace(ServiceDescriptor.Singleton<IVectorStore>(_ => new QdrantVectorStore(config)));
        });
        return builder;
    }
}
