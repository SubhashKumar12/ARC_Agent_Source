using ARC.Data.Exceptions;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql;

public sealed class RecoveryCaseRepository : IRecoveryCaseRepository
{
    private readonly IStoredProcedureExecutor _procedures;
    private readonly SqlStoredProcedureNames _spNames;

    public RecoveryCaseRepository(
        IStoredProcedureExecutor procedures,
        IOptions<SqlStoredProcedureNames> spNames)
    {
        _procedures = procedures;
        _spNames = spNames.Value;
    }

    public async Task UpsertIndexAsync(RecoveryCaseIndex index, CancellationToken cancellationToken)
    {
        try
        {
            await _procedures.ExecuteAsync(
                _spNames.UpsertRecoveryCaseIndex,
                new
                {
                    CycleId = index.CycleId.Value,
                    DealerUrn = index.DealerUrn.Value,
                    index.Status,
                    index.CorrelationId,
                    index.WaitingGate,
                    index.UpdatedUtc,
                    index.RecoverabilityScore,
                    index.RecoveryTier
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to persist recovery case index.", ex);
        }
    }

    public async Task<RecoveryCaseIndex?> GetAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        var row = await _procedures.QuerySingleOrDefaultAsync<IndexRow>(
            _spNames.GetRecoveryCaseIndex,
            new { CycleId = cycleId.Value, DealerUrn = dealerUrn.Value },
            cancellationToken: cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<RecoveryCaseIndex>> ListByCycleAsync(
        CycleId cycleId, string? region, string? depot, CancellationToken cancellationToken)
    {
        var rows = await _procedures.QueryAsync<IndexRow>(
            _spNames.ListRecoveryCasesByCycle,
            new
            {
                CycleId = cycleId.Value,
                Region = string.IsNullOrWhiteSpace(region) ? null : region,
                Depot = string.IsNullOrWhiteSpace(depot) ? null : depot
            },
            cancellationToken: cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<RankedWorklist> ListRankedWorklistAsync(
        CycleId cycleId, string? region, string? depot, bool topDecile, CancellationToken cancellationToken)
    {
        var rows = await _procedures.QueryAsync<WorklistRow>(
            _spNames.GetRankedWorklist,
            new
            {
                CycleId = cycleId.Value,
                Region = string.IsNullOrWhiteSpace(region) ? null : region,
                Depot = string.IsNullOrWhiteSpace(depot) ? null : depot,
                TopDecile = topDecile
            },
            cancellationToken: cancellationToken);

        var list = rows.ToList();
        var eligibleCount = list.Count == 0 ? 0 : list[0].EligibleCount;
        var entries = list.Select(r => new RankedWorklistEntry(
            (int)r.RankNo,
            new DealerUrn(r.DealerUrn),
            r.RecoverabilityScore,
            r.RecoveryTier,
            r.Status,
            r.WaitingGate)).ToList();
        return new RankedWorklist(cycleId, topDecile, eligibleCount, entries);
    }

    private sealed class IndexRow
    {
        public string CycleId { get; set; } = "";
        public string DealerUrn { get; set; } = "";
        public string Status { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public string? WaitingGate { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public decimal? RecoverabilityScore { get; set; }
        public string? RecoveryTier { get; set; }

        public RecoveryCaseIndex ToDomain() => new(
            new CycleId(CycleId),
            new DealerUrn(DealerUrn),
            Status,
            CorrelationId,
            WaitingGate,
            UpdatedUtc,
            RecoverabilityScore,
            RecoveryTier);
    }

    private sealed class WorklistRow
    {
        public string DealerUrn { get; set; } = "";
        public decimal RecoverabilityScore { get; set; }
        public string RecoveryTier { get; set; } = "";
        public string Status { get; set; } = "";
        public string? WaitingGate { get; set; }
        public long RankNo { get; set; }
        public int EligibleCount { get; set; }
    }
}
