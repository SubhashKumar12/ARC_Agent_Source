using ARC.Domain.Odos;
using ARC.Domain.ValueObjects;

namespace ARC.Data.A1;

/// <summary>
/// Production SQL adapter contract for <c>ODOS.odos_opening_data</c>.
/// Not registered by default — bind when production SQL is available.
/// Does not invent columns; uses the confirmed ODOS opening shape only.
/// </summary>
public sealed class SqlOdosOpeningDataSource : IOdosOpeningDataSource
{
    // Intentionally not wired to Dapper here: ReferenceSchema does not host ODOS.* tables,
    // and inventing them would violate the production-source rule. Callers supply a reader.

    private readonly Func<DealerUrn, CancellationToken, Task<IReadOnlyList<OdosOpeningSnapshot>>> _listByDealer;

    public SqlOdosOpeningDataSource(
        Func<DealerUrn, CancellationToken, Task<IReadOnlyList<OdosOpeningSnapshot>>> listByDealer)
        => _listByDealer = listByDealer ?? throw new ArgumentNullException(nameof(listByDealer));

    public Task<IReadOnlyList<OdosOpeningSnapshot>> ListByDealerAsync(
        DealerUrn dealerUrn,
        CancellationToken cancellationToken)
        => _listByDealer(dealerUrn, cancellationToken);

    public async Task<OdosOpeningSnapshot?> GetLatestAsync(
        DealerUrn dealerUrn,
        CancellationToken cancellationToken)
    {
        var rows = await ListByDealerAsync(dealerUrn, cancellationToken);
        return rows
            .OrderByDescending(r => r.Year)
            .ThenByDescending(r => r.Month)
            .FirstOrDefault();
    }
}

/// <summary>Empty adjustment source for production until assignment-only facts have real SoRs.</summary>
public sealed class EmptyLedgerAdjustmentFactSource : ILedgerAdjustmentFactSource
{
    public static EmptyLedgerAdjustmentFactSource Instance { get; } = new();

    public Task<IReadOnlyList<LedgerAdjustmentFact>> ListByDealerAsync(
        DealerUrn dealerUrn,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LedgerAdjustmentFact>>(Array.Empty<LedgerAdjustmentFact>());
}
