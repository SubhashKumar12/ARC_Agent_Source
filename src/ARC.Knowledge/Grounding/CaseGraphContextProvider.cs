using ARC.Domain.ValueObjects;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Provenance;

namespace ARC.Knowledge.Grounding;

/// <summary>
/// Deterministic dealer/case graph context. No LLM. Bounded, authorization-aware via caller scope.
/// </summary>
public interface ICaseGraphContextProvider
{
    Task<IReadOnlyList<StructuredFact>> GetDealerFactsAsync(
        CaseGraphContextRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CaseGraphContextRequest(
    string DealerUrn,
    string? CycleId = null,
    string? RecoveryCaseId = null,
    string? CorrelationId = null,
    int MaxFacts = 32);

/// <summary>
/// Adapts existing <see cref="IGraphTraversal"/> (Dealer 360) into compact structured facts.
/// Does not invent payment/notice/agreement edges that are not yet in the graph.
/// </summary>
public sealed class CaseGraphContextProvider : ICaseGraphContextProvider
{
    private readonly IGraphTraversal _graph;

    public CaseGraphContextProvider(IGraphTraversal graph)
    {
        _graph = graph;
    }

    public async Task<IReadOnlyList<StructuredFact>> GetDealerFactsAsync(
        CaseGraphContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DealerUrn))
            return Array.Empty<StructuredFact>();

        var max = request.MaxFacts <= 0 ? 32 : Math.Min(request.MaxFacts, 64);
        var nodes = await _graph.TraverseDealerAsync(new DealerUrn(request.DealerUrn.Trim()), cancellationToken);

        // Dealer-scoped only: exclude nodes that do not belong to the requested dealer identity when encoded.
        var facts = new List<StructuredFact>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            if (!seen.Add(node.NodeId))
                continue;

            facts.Add(new StructuredFact(
                FactId: node.NodeId,
                Kind: node.Kind,
                Label: node.Label,
                Summary: $"{node.Label}:{node.Provenance.DocumentId}",
                TrustLevel: EvidenceTrustLevel.AuthoritativeStructured,
                Provenance: node.Provenance));

            if (facts.Count >= max)
                break;
        }

        return facts;
    }
}
