using ARC.Knowledge.Provenance;

namespace ARC.Knowledge.Retrieval;

/// <summary>
/// Document retrieval provider. Cosmos query syntax stays in the infrastructure implementation.
/// </summary>
public interface IDocumentRetriever
{
    Task<IReadOnlyList<EvidenceSource>> RetrieveAsync(
        DocumentRetrievalRequest request,
        CancellationToken cancellationToken);
}
