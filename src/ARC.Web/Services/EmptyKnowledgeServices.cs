using ARC.Data.Sql;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Retrieval;

namespace ARC.Web.Services;

/// <summary>Empty knowledge retrieval for demo mode (real grounding providers still registered).</summary>
public sealed class EmptyKnowledgeRetrievalService : IKnowledgeRetrievalService
{
    public Task<RetrievalResult> RetrieveAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new RetrievalResult([], [], DateTimeOffset.UtcNow));
}

/// <summary>Empty graph traversal for demo mode.</summary>
public sealed class EmptyGraphTraversal : IGraphTraversal
{
    public Task<IReadOnlyList<GraphNode>> TraverseDealerAsync(
        DealerUrn dealerUrn,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<GraphNode>>([]);
}

