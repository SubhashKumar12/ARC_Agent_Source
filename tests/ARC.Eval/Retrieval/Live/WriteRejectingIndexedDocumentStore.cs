using ARC.Knowledge.Ingestion;
using ARC.Knowledge.Vector;

namespace ARC.Eval.Retrieval.Live;

/// <summary>
/// Live evaluation must not ingest or upsert documents. Any write fails closed.
/// </summary>
public sealed class WriteRejectingIndexedDocumentStore : IIndexedDocumentStore
{
    public int UpsertCount { get; private set; }

    public Task<IndexedDocument?> FindByContentHashAsync(
        string contentHash,
        string embeddingModel,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IndexedDocument?>(null);

    public Task UpsertAsync(IndexedDocument document, CancellationToken cancellationToken = default)
    {
        UpsertCount++;
        throw new InvalidOperationException("Live retrieval evaluation is read-only and must not write documents.");
    }

    public Task UpsertBatchAsync(IEnumerable<IndexedDocument> documents, CancellationToken cancellationToken = default)
    {
        UpsertCount++;
        throw new InvalidOperationException("Live retrieval evaluation is read-only and must not write documents.");
    }

    public Task<IReadOnlyList<IndexedDocument>> QueryByDocumentTypeAsync(
        string documentType,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IndexedDocument>>([]);

    public Task<IReadOnlyList<IndexedDocument>> QueryBySourceAndVersionAsync(
        string documentType,
        string sourceDocumentId,
        string version,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IndexedDocument>>([]);
}
