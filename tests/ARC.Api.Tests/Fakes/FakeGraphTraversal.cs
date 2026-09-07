using ARC.Domain.ValueObjects;
using ARC.Knowledge.Graph;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test fake for graph traversal. Returns empty graph data.
/// </summary>
public sealed class FakeGraphTraversal : IGraphTraversal
{
    public Task<IReadOnlyList<GraphNode>> TraverseDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<GraphNode>>([]);
    }
}
