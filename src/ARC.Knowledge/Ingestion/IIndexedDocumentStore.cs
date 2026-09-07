using ARC.Knowledge.Vector;

namespace ARC.Knowledge.Ingestion;

/// <summary>
/// Abstraction for persisting and retrieving indexed document chunks.
/// Decouples ingestion orchestration from concrete storage implementation (Cosmos, etc.).
/// </summary>
public interface IIndexedDocumentStore
{
    /// <summary>
    /// Finds an existing indexed document by content hash and embedding model.
    /// Returns null if not found.
    /// </summary>
    Task<IndexedDocument?> FindByContentHashAsync(
        string contentHash,
        string embeddingModel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts an indexed document. Uses id as unique key.
    /// </summary>
    Task UpsertAsync(
        IndexedDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts multiple indexed documents in a batch.
    /// </summary>
    Task UpsertBatchAsync(
        IEnumerable<IndexedDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries documents by document type (partition key).
    /// </summary>
    Task<IReadOnlyList<IndexedDocument>> QueryByDocumentTypeAsync(
        string documentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries chunks for one source document and version within a documentType partition.
    /// Used for same-version lifecycle retirement without cross-document side effects.
    /// </summary>
    Task<IReadOnlyList<IndexedDocument>> QueryBySourceAndVersionAsync(
        string documentType,
        string sourceDocumentId,
        string version,
        CancellationToken cancellationToken = default);
}
