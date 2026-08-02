# XiaoZhi.Net.Server RAG

The RAG capability is split into independent assemblies so that the server runtime only depends on retrieval contracts and the selected vector-store implementation. Document parsers and their package dependencies are isolated from `XiaoZhi.Net.Server`.

| Assembly | Responsibility | Not referenced by the server runtime |
| --- | --- | --- |
| `XiaoZhi.Net.Server.RAG.Abstractions` | Vector-store contracts and RAG data models. | No |
| `XiaoZhi.Net.Server.RAG` | In-memory store, indexing, retrieval and MAF `TextSearchProvider` adapter. | No |
| `XiaoZhi.Net.Server.RAG.Qdrant` | Optional Qdrant implementation of `IVectorStore`. | No |
| `XiaoZhi.Net.Server.RAG.Ingestion` | Text, Markdown, PDF, DOCX, XLSX and CSV extraction plus chunking/import. | Yes |
| `XiaoZhi.Net.Sample.Server.RAGIngestion` | Standalone command-line knowledge-base builder example. | Yes |

`XiaoZhi.Net.Server` references the core and optional Qdrant store, but does not reference Ingestion, MiniExcel, Open XML, or PdfPig packages.

## Runtime registration

Register an embedding generator before adding RAG. The default is an in-memory vector store, intended for development and demos.

```csharp
services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingGenerator);
services.AddInMemoryRag();
```

Use Qdrant by registering the core services then selecting the store:

```csharp
services.AddRagCore();
services.AddQdrantVectorStore(new QdrantVectorStoreOptions
{
    Host = "localhost",
    Port = 6334,
    CollectionName = "xiaozhi_knowledge"
});
```

`IRagRetriever` is the provider-facing query contract; `IKnowledgeBaseIndexer` indexes complete documents using the default chunker. The Server-side MAF context provider adapts retrieval results to `TextSearchProvider`, keeping agents independent of a concrete vector database.

## XiaoZhi.Net.Server integration

RAG is optional. Add a `RAG` profile to `SelectedSettings` and `ConfiguredSettings`; its absence leaves the existing Server behavior unchanged. In `Memory` mode, the global Resource imports only TXT and Markdown during startup. In `Qdrant` mode, use the standalone sample below to ingest documents beforehand.

```jsonc
"SelectedSettings": {
  "RAG": "LocalKnowledge"
},
"ConfiguredSettings": {
  "RAG": {
    "LocalKnowledge": {
      "StoreType": "Memory",
      "KnowledgeBaseId": "default",
      "SourceDirectory": "./knowledge",
      "EmbeddingBaseUrl": "https://your-openai-compatible-endpoint/v1/",
      "EmbeddingApiKey": "your-api-key",
      "EmbeddingModelName": "text-embedding-3-small",
      "TopK": 5,
      "MinimumScore": 0.65
    }
  }
}
```

Set `StoreType` to `Qdrant` and supply `QdrantHost`, `QdrantPort`, `QdrantUseTls`, `QdrantApiKey`, and `QdrantCollectionName` for the persistent store. For `Custom`, implement `IVectorStore` and call `serverBuilder.WithRagVectorStore<YourVectorStore>()`.

## Build a knowledge base

The sample supports `.txt`, `.md`, `.markdown`, `.pdf`, `.docx`, `.xlsx`, and `.csv`. Configure `appsettings.json` in `XiaoZhi.Net.Sample.Server.RAGIngestion` with an OpenAI-compatible embedding endpoint, key, and model.

```powershell
dotnet run --project demo/XiaoZhi.Net.Sample.Server.RAGIngestion -- --input C:\knowledge --knowledge-base manuals --store memory
```

For Qdrant, set `RagImport:Store` to `qdrant` and provide the nested `Qdrant` options. Imports are idempotent: an unchanged source file is skipped; an updated source replaces its old chunks after new chunks have been written successfully.

DOCX extraction uses Open XML because MiniWord is a document-generation library rather than a reader. XLSX/CSV extraction uses MiniExcel. PDF extraction uses PdfPig.
