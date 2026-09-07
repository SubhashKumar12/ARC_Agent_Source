using ARC.Knowledge.Ingestion;
using ARC.Knowledge.Vector;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test fake for indexed document store. Does not connect to real Cosmos DB.
/// </summary>
public sealed class FakeIndexedDocumentStore : IIndexedDocumentStore
{
    public Task<IndexedDocument?> FindByContentHashAsync(string contentHash, string embeddingModel, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IndexedDocument?>(null);
    }

    public Task UpsertAsync(IndexedDocument document, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task UpsertBatchAsync(IEnumerable<IndexedDocument> documents, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IndexedDocument>> QueryByDocumentTypeAsync(string documentType, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<IndexedDocument>>([]);
    }

    public Task<IReadOnlyList<IndexedDocument>> QueryBySourceAndVersionAsync(string documentType, string sourceDocumentId, string version, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<IndexedDocument>>([]);
    }
}
