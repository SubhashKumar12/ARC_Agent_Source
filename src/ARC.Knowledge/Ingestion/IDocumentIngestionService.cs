using ARC.Knowledge.Chunking;

namespace ARC.Knowledge.Ingestion;

/// <summary>
/// Orchestrates document ingestion: chunking, embedding, deduplication, and persistence.
/// Reusable across hosting environments (Function, CLI, scheduled job).
/// </summary>
public interface IDocumentIngestionService
{
    /// <summary>
    /// Ingests a single source document into the indexed document store.
    /// Idempotent: running twice on same document does not create duplicates.
    /// </summary>
    Task<IngestionResult> IngestDocumentAsync(
        SourceDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests multiple source documents in batch.
    /// </summary>
    Task<IngestionResult> IngestDocumentsAsync(
        IEnumerable<SourceDocument> documents,
        CancellationToken cancellationToken = default);
}
