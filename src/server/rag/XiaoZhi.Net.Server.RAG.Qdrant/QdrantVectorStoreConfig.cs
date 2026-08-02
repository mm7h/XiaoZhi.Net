namespace XiaoZhi.Net.Server.RAG.Qdrant
{
    public class QdrantVectorStoreConfig
    {
        public string Host { get; init; } = "localhost";
        public int Port { get; init; } = 6334;
        public bool UseTls { get; init; }
        public string? ApiKey { get; init; }
        public string CollectionName { get; init; } = "xiaozhi_knowledge";
    }
}
