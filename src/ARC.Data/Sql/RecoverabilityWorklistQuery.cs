using ARC.Domain.Entities;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Sql;

/// <summary>
/// In-memory ranking identical to SQL: score DESC, DealerUrn ordinal ASC, then top-decile take.
/// Production worklist is SQL; this keeps CLI/unit tests aligned.
/// </summary>
public static class RecoverabilityWorklistQuery
{
    public static RankedWorklist FromIndex(
        CycleId cycleId,
        bool topDecile,
        IEnumerable<RecoveryCaseIndex> rows,
        IReadOnlyDictionary<string, Dealer>? dealers = null,
        string? region = null,
        string? depot = null)
    {
        var eligible = rows
            .Where(r => MatchesScope(r, dealers, region, depot))
            .Where(r => RecoverabilityWorklist.IsRankable(r.RecoverabilityScore, r.RecoveryTier, r.Status))
            .OrderByDescending(r => r.RecoverabilityScore)
            .ThenBy(r => r.DealerUrn.Value, StringComparer.Ordinal)
            .ToList();

        var take = topDecile ? RecoverabilityWorklist.TopDecileSize(eligible.Count) : eligible.Count;
        var entries = eligible
            .Take(take)
            .Select((r, i) => new RankedWorklistEntry(
                i + 1,
                r.DealerUrn,
                r.RecoverabilityScore!.Value,
                r.RecoveryTier!,
                r.Status,
                r.WaitingGate))
            .ToList();

        return new RankedWorklist(cycleId, topDecile, eligible.Count, entries);
    }

    private static bool MatchesScope(
        RecoveryCaseIndex row,
        IReadOnlyDictionary<string, Dealer>? dealers,
        string? region,
        string? depot)
    {
        if (dealers is null)
            return string.IsNullOrWhiteSpace(region) && string.IsNullOrWhiteSpace(depot);
        if (!dealers.TryGetValue(row.DealerUrn.Value, out var dealer))
            return false;
        if (!string.IsNullOrWhiteSpace(region)
            && !string.Equals(dealer.Region, region, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(depot)
            && !string.Equals(dealer.Depot, depot, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
