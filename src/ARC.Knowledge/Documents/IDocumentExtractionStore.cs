namespace ARC.Knowledge.Documents;

/// <summary>
/// Smallest additive cache for Document Intelligence results.
/// Not a second evidence repository — blobs remain on <c>IEvidenceDocumentRepository</c>.
/// </summary>
public interface IDocumentExtractionStore
{
    Task<PersistedDocumentExtraction?> GetAsync(string contentHash, string modelId, CancellationToken cancellationToken);

    Task SaveAsync(PersistedDocumentExtraction extraction, CancellationToken cancellationToken);
}
