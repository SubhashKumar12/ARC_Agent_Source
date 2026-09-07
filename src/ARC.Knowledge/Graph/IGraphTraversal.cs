using ARC.Domain.ValueObjects;
using ARC.Knowledge.Provenance;

namespace ARC.Knowledge.Graph;

public sealed record GraphNode(
    string NodeId,
    string Label,
    string Kind,
    SourceReference Provenance);

public interface IGraphTraversal
{
    Task<IReadOnlyList<GraphNode>> TraverseDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken);
}
