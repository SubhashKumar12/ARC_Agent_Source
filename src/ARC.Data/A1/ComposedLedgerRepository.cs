using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Odos;
using ARC.Domain.ValueObjects;

namespace ARC.Data.A1;

/// <summary>
/// A1 ledger adapter: composes opening + adjustment sources into <see cref="ILedgerRepository"/>.
/// SQL and synthetic backends plug the same contracts without Domain knowing the provider.
/// </summary>
public sealed class ComposedLedgerRepository : ILedgerRepository
{
    private readonly IOdosOpeningDataSource _opening;
    private readonly ILedgerAdjustmentFactSource _adjustments;
    private readonly bool _openingIsSynthetic;

    public ComposedLedgerRepository(
        IOdosOpeningDataSource opening,
        ILedgerAdjustmentFactSource adjustments,
        bool openingIsSynthetic)
    {
        _opening = opening;
        _adjustments = adjustments;
        _openingIsSynthetic = openingIsSynthetic;
    }

    public async Task<IReadOnlyList<LedgerPosition>> ListByDealerAsync(
        DealerUrn urn,
        CancellationToken cancellationToken)
    {
        var latest = await _opening.GetLatestAsync(urn, cancellationToken);
        var adjustments = await _adjustments.ListByDealerAsync(urn, cancellationToken);
        return OdosOpeningLedgerProjector.Project(urn, latest, adjustments, _openingIsSynthetic);
    }
}
