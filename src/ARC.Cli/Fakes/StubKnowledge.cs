using ARC.Domain.ValueObjects;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;

namespace ARC.Cli.Fakes;

internal sealed class EmptyKnowledgeRetrievalService : IKnowledgeRetrievalService
{
    public Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new RetrievalResult([], [], DateTimeOffset.UtcNow));
}

internal sealed class EmptyGraphTraversal : IGraphTraversal
{
    public Task<IReadOnlyList<GraphNode>> TraverseDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GraphNode>>([]);
}
