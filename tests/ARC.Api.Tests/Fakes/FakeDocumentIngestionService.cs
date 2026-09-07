using ARC.Knowledge.Chunking;
using ARC.Knowledge.Ingestion;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test fake for document ingestion. Does not perform real ingestion.
/// </summary>
public sealed class FakeDocumentIngestionService : IDocumentIngestionService
{
    public Task<IngestionResult> IngestDocumentAsync(SourceDocument document, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new IngestionResult
        {
            DocumentsProcessed = 0,
            ChunksProduced = 0,
            ChunksEmbedded = 0,
            ChunksSkippedUnchanged = 0,
            ChunksFailed = 1,
            Duration = TimeSpan.Zero,
            Errors = new() { "Test fake: Ingestion not available" }
        });
    }

    public Task<IngestionResult> IngestDocumentsAsync(IEnumerable<SourceDocument> documents, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new IngestionResult
        {
            DocumentsProcessed = 0,
            ChunksProduced = 0,
            ChunksEmbedded = 0,
            ChunksSkippedUnchanged = 0,
            ChunksFailed = 1,
            Duration = TimeSpan.Zero,
            Errors = new() { "Test fake: Ingestion not available" }
        });
    }
}
