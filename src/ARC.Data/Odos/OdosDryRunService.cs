using ARC.Domain.Odos;

namespace ARC.Data.Odos;

/// <summary>One dry-run line. Deliberately excludes names, addresses and contact details.</summary>
public sealed record OdosDryRunLine(
    string DepotCode,
    string DealerCode,
    string? BusinessLine,
    decimal CurrentOutstanding,
    decimal OsAmt0,
    decimal OsAmt1,
    decimal OsAmt2,
    decimal OsAmt3,
    decimal OsAmt4,
    decimal BusinessLimit,
    bool EligibleByCurrentOdosLimit);

public sealed record OdosDryRunResult(
    int Year,
    int Month,
    string? DepotCode,
    string? DealerCode,
    int RowsReturned,
    int SampleSize,
    decimal DefaultBusinessLimit,
    IReadOnlyList<OdosDryRunLine> Sample);

/// <summary>
/// Read-only ODOS production dry run. Reads opening data and the applicable business-line limit,
/// then reports eligibility. Performs no writes, issues no notice, creates no legal action
/// and creates no workflow state.
/// </summary>
public interface IOdosDryRunService
{
    Task<OdosDryRunResult> RunAsync(
        OdosOpeningQuery query,
        int sampleSize,
        CancellationToken cancellationToken);
}

internal sealed class OdosDryRunService : IOdosDryRunService
{
    private readonly IOdosOpeningQuerySource _opening;
    private readonly IOdosBusinessLineLimitSource _limits;

    public OdosDryRunService(
        IOdosOpeningQuerySource opening,
        IOdosBusinessLineLimitSource limits)
    {
        _opening = opening;
        _limits = limits;
    }

    public async Task<OdosDryRunResult> RunAsync(
        OdosOpeningQuery query,
        int sampleSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (sampleSize < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleSize), "Sample size must be at least 1.");

        var rows = await _opening.QueryAsync(query, cancellationToken);
        var sample = rows.Take(sampleSize).ToList();

        // The stored procedure owns the business-line lookup and its default fallback.
        var limitProvider = await PrefetchedOdosBusinessLineLimitProvider.CreateAsync(
            _limits,
            sample.Select(r => r.BusinessLine),
            cancellationToken);

        var lines = sample.Select(row =>
        {
            var limit = OdosEligibility.ResolveLimit(row.BusinessLine, limitProvider);
            var eligible = OdosEligibility.ExceedsOutstandingLimit(row.OsAmtUpdt, row.BusinessLine, limitProvider);

            return new OdosDryRunLine(
                row.DepotCode,
                row.DealerCode,
                row.BusinessLine,
                row.OsAmtUpdt,
                row.OsAmt0,
                row.OsAmt1,
                row.OsAmt2,
                row.OsAmt3,
                row.OsAmt4,
                limit,
                eligible);
        }).ToList();

        return new OdosDryRunResult(
            query.Year,
            query.Month,
            query.DepotCode,
            query.DealerCode,
            rows.Count,
            lines.Count,
            limitProvider.GetDefaultLimit(),
            lines);
    }
}
