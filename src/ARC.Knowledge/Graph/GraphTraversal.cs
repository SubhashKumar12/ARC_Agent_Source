using Microsoft.Extensions.Logging;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Provenance;

namespace ARC.Knowledge.Graph;

/// <summary>Dealer 360 from SQL facts. Does not score recoverability or decide notices.</summary>
public sealed class GraphTraversal : IGraphTraversal
{
    private readonly IDealerRepository _dealers;
    private readonly IChequeRepository _cheques;
    private readonly ILedgerRepository _ledger;
    private readonly ILogger<GraphTraversal> _logger;

    public GraphTraversal(
        IDealerRepository dealers,
        IChequeRepository cheques,
        ILedgerRepository ledger,
        ILogger<GraphTraversal> logger)
    {
        _dealers = dealers;
        _cheques = cheques;
        _ledger = ledger;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GraphNode>> TraverseDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var nodes = new List<GraphNode>();

        var dealer = await _dealers.GetAsync(dealerUrn, cancellationToken);
        if (dealer is null)
            return nodes;

        nodes.Add(Node("dealer", dealer.Urn.Value, "Dealer", dealer.Urn.Value, now));
        if (dealer.Depot is not null)
            nodes.Add(Node("depot", dealer.Depot, "Depot", dealer.Urn.Value, now));
        if (dealer.Region is not null)
            nodes.Add(Node("region", dealer.Region, "Region", dealer.Urn.Value, now));
        if (dealer.CoveringTsi is not null)
            nodes.Add(Node("tsi", dealer.CoveringTsi, "TSI", dealer.Urn.Value, now));

        foreach (var cheque in await _cheques.ListChequesAsync(dealerUrn, cancellationToken))
            nodes.Add(Node("cheque", cheque.ChequeNumber, "SecurityCheque", dealer.Urn.Value, now));

        foreach (var memo in await _cheques.ListReturnMemosAsync(dealerUrn, cancellationToken))
            nodes.Add(Node("memo", memo.ChequeNumber, "ChequeReturnMemo", dealer.Urn.Value, now));

        var lines = await _ledger.ListByDealerAsync(dealerUrn, cancellationToken);
        nodes.Add(Node("ledger", $"{lines.Count} positions", "LedgerPosition", dealer.Urn.Value, now));

        _logger.LogInformation("Graph traversal for dealer {DealerUrn} returned {Count} nodes", dealerUrn.Value, nodes.Count);
        return nodes;
    }

    private static GraphNode Node(string kind, string id, string label, string dealerUrn, DateTimeOffset utc)
        => new(
            $"{kind}:{id}",
            label,
            kind,
            new SourceReference(id, null, null, null, null, "sql", utc));
}
