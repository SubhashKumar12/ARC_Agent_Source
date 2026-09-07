using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test fake for document retrieval. Returns empty results.
/// </summary>
public sealed class FakeDocumentRetriever : IDocumentRetriever
{
    public Task<IReadOnlyList<EvidenceSource>> RetrieveAsync(
        DocumentRetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<EvidenceSource>>([]);
    }
}
