using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;

namespace ARC.Eval.Retrieval;

/// <summary>
/// Decorator over a production <see cref="IDocumentRetriever"/>. Records calls; does not implement ranking.
/// </summary>
public sealed class RecordingDocumentRetriever : IDocumentRetriever
{
    private readonly IDocumentRetriever _inner;

    public RecordingDocumentRetriever(IDocumentRetriever inner)
    {
        _inner = inner;
        InnerTypeName = inner.GetType().Name;
    }

    public string InnerTypeName { get; }

    public int QueryCount { get; private set; }

    public int WriteCount { get; } = 0;

    public IReadOnlyList<DocumentRetrievalRequest> Calls => _calls;

    private readonly List<DocumentRetrievalRequest> _calls = [];

    public DocumentRetrievalRequest? Last => _calls.Count == 0 ? null : _calls[^1];

    public void ResetCounters()
    {
        QueryCount = 0;
        _calls.Clear();
    }

    public async Task<IReadOnlyList<EvidenceSource>> RetrieveAsync(
        DocumentRetrievalRequest request,
        CancellationToken cancellationToken)
    {
        QueryCount++;
        _calls.Add(request);
        return await _inner.RetrieveAsync(request, cancellationToken);
    }
}
