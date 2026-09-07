using ARC.Domain.ValueObjects;

namespace ARC.Domain.Odos;

/// <summary>
/// A1 input contract: opening outstanding facts for a dealer.
/// Production SQL and synthetic adapters both implement this without Domain coupling to providers.
/// </summary>
public interface IOdosOpeningDataSource
{
    Task<IReadOnlyList<OdosOpeningSnapshot>> ListByDealerAsync(
        DealerUrn dealerUrn,
        CancellationToken cancellationToken);

    Task<OdosOpeningSnapshot?> GetLatestAsync(
        DealerUrn dealerUrn,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional A1 adjustment facts (credits, rebates, returns, clearing, disputes).
/// Production may leave these empty until source systems exist; synthetic supplies
/// assignment-evaluation-only rows.
/// </summary>
public interface ILedgerAdjustmentFactSource
{
    Task<IReadOnlyList<LedgerAdjustmentFact>> ListByDealerAsync(
        DealerUrn dealerUrn,
        CancellationToken cancellationToken);
}

/// <summary>Provider-agnostic adjustment row consumed by the A1 ledger projector.</summary>
public sealed record LedgerAdjustmentFact(
    DealerUrn DealerUrn,
    string DocumentType,
    DateOnly DueDate,
    DateOnly PostedOn,
    Money Amount,
    LineItemRef Lineage,
    bool IsAssignmentEvaluationOnly);
