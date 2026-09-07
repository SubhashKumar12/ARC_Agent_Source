using ARC.Data.A1;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Odos;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Synthetic;

/// <summary>
/// Synthetic / Assignment Evaluation Only — in-memory adapters feeding the same A1 contracts
/// as production SQL (<see cref="IDealerRepository"/>, <see cref="ILedgerRepository"/>, etc.).
/// </summary>
public sealed class SyntheticDatasetStore :
    IDealerRepository,
    ILedgerRepository,
    IChequeRepository,
    IOdosOpeningDataSource,
    ILedgerAdjustmentFactSource
{
    private readonly SyntheticDataset _dataset;
    private readonly Dictionary<string, SyntheticDealerProfile> _dealers;
    private readonly Dictionary<string, List<OdosOpeningSnapshot>> _openings;
    private readonly Dictionary<string, List<LedgerAdjustmentFact>> _adjustments;
    private readonly Dictionary<string, List<SecurityCheque>> _cheques;
    private readonly Dictionary<string, List<ChequeReturnMemo>> _memos;
    private readonly ComposedLedgerRepository _ledger;

    public SyntheticDataset Dataset => _dataset;
    public SyntheticR6LineageCase R6LineageCase => _dataset.R6LineageCase;

    public SyntheticDatasetStore(SyntheticDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _dealers = dataset.Dealers.ToDictionary(d => d.CanonicalUrn.Value, StringComparer.OrdinalIgnoreCase);
        _openings = dataset.OpeningHistory
            .GroupBy(o => o.DealerUrn.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Snapshot).ToList(), StringComparer.OrdinalIgnoreCase);
        _adjustments = dataset.Adjustments
            .GroupBy(a => a.DealerUrn.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        _cheques = dataset.Cheques
            .GroupBy(c => c.DealerUrn.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        _memos = dataset.ReturnMemos
            .GroupBy(m => m.DealerUrn.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        _ledger = new ComposedLedgerRepository(this, this, openingIsSynthetic: true);
    }

    public static SyntheticDatasetStore CreateDefault(int seed = 112_026_11, int dealerCount = 2_500)
        => new(new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions
        {
            Seed = seed,
            DealerCount = dealerCount
        }));

    public Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        _dealers.TryGetValue(urn.Value, out var profile);
        return Task.FromResult(profile?.ToDomain());
    }

    public Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken)
    {
        var list = _dataset.Dealers
            .Where(d => string.Equals(d.Region, region, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.ToDomain())
            .ToList();
        return Task.FromResult<IReadOnlyList<Dealer>>(list);
    }

    public Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Dealer>>(_dataset.Dealers.Select(d => d.ToDomain()).ToList());

    public Task<IReadOnlyList<LedgerPosition>> ListByDealerAsync(DealerUrn urn, CancellationToken cancellationToken)
        => _ledger.ListByDealerAsync(urn, cancellationToken);

    public Task<IReadOnlyList<SecurityCheque>> ListChequesAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        if (!_cheques.TryGetValue(urn.Value, out var list))
            return Task.FromResult<IReadOnlyList<SecurityCheque>>(Array.Empty<SecurityCheque>());
        return Task.FromResult<IReadOnlyList<SecurityCheque>>(list);
    }

    public Task<IReadOnlyList<ChequeReturnMemo>> ListReturnMemosAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        if (!_memos.TryGetValue(urn.Value, out var list))
            return Task.FromResult<IReadOnlyList<ChequeReturnMemo>>(Array.Empty<ChequeReturnMemo>());
        return Task.FromResult<IReadOnlyList<ChequeReturnMemo>>(list);
    }

    async Task<IReadOnlyList<OdosOpeningSnapshot>> IOdosOpeningDataSource.ListByDealerAsync(
        DealerUrn dealerUrn,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        if (!_openings.TryGetValue(dealerUrn.Value, out var list))
            return Array.Empty<OdosOpeningSnapshot>();
        return list;
    }

    public Task<OdosOpeningSnapshot?> GetLatestAsync(DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        if (!_openings.TryGetValue(dealerUrn.Value, out var list) || list.Count == 0)
            return Task.FromResult<OdosOpeningSnapshot?>(null);

        var latest = list
            .OrderByDescending(r => r.Year)
            .ThenByDescending(r => r.Month)
            .First();
        return Task.FromResult<OdosOpeningSnapshot?>(latest);
    }

    Task<IReadOnlyList<LedgerAdjustmentFact>> ILedgerAdjustmentFactSource.ListByDealerAsync(
        DealerUrn dealerUrn,
        CancellationToken cancellationToken)
    {
        if (!_adjustments.TryGetValue(dealerUrn.Value, out var list))
            return Task.FromResult<IReadOnlyList<LedgerAdjustmentFact>>(Array.Empty<LedgerAdjustmentFact>());
        return Task.FromResult<IReadOnlyList<LedgerAdjustmentFact>>(list);
    }

    public Dispute? GetDispute(DealerUrn urn)
        => _dataset.Disputes.FirstOrDefault(d => d.DealerUrn.Equals(urn));

    public SyntheticPtpFact? GetPtp(DealerUrn urn)
        => _dataset.Ptps.FirstOrDefault(p => p.DealerUrn.Equals(urn));
}
