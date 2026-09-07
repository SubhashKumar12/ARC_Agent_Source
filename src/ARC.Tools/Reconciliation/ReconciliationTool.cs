using Microsoft.Extensions.Logging;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Identity;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;

namespace ARC.Tools.Reconciliation;

public sealed record ComputeNetExposureRequest(
    string DealerUrn,
    DateOnly AsOf,
    string? CycleId,
    string? CorrelationId);

public sealed record ComputeNetExposureResult(
    ExposureBreakdown Exposure,
    int LedgerLineCount,
    bool DealerUnderMoratorium);

/// <summary>Authoritative claim amount. Source tool name: ComputeNetExposure. No SQL in this class.</summary>
public sealed class ReconciliationTool
{
    public const string Name = "ComputeNetExposure";

    private readonly IDealerRepository _dealers;
    private readonly ILedgerRepository _ledger;
    private readonly ILogger<ReconciliationTool> _logger;

    public ReconciliationTool(
        IDealerRepository dealers,
        ILedgerRepository ledger,
        ILogger<ReconciliationTool> logger)
    {
        _dealers = dealers;
        _ledger = ledger;
        _logger = logger;
    }

    /// <summary>
    /// Fail-closed entry: unresolved or ambiguous identity must not compute merged exposure.
    /// Existing <see cref="ComputeNetExposureAsync"/> is unchanged for already-canonical URNs.
    /// </summary>
    public async Task<ComputeNetExposureResult?> ComputeNetExposureIfIdentityResolvedAsync(
        DealerIdentityResolution resolution,
        DateOnly asOf,
        CancellationToken cancellationToken,
        string? cycleId = null,
        string? correlationId = null)
    {
        if (!DealerIdentityProgression.AllowsExposure(resolution) || resolution.CanonicalUrn is null)
            return null;

        return await ComputeNetExposureAsync(
            new ComputeNetExposureRequest(resolution.CanonicalUrn.Value, asOf, cycleId, correlationId),
            cancellationToken);
    }

    public async Task<ComputeNetExposureResult> ComputeNetExposureAsync(
        ComputeNetExposureRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(request.DealerUrn))
            throw new ToolException(Name, "DealerUrn is required.");

        try
        {
            var urn = new DealerUrn(request.DealerUrn);
            var dealer = await _dealers.GetAsync(urn, cancellationToken)
                ?? throw new DealerNotFoundException(request.DealerUrn);

            var lines = await _ledger.ListByDealerAsync(urn, cancellationToken);
            var buckets = Classify(lines);
            var unknown = lines.Where(l => ClassifyOne(l.DocumentType) is null).ToList();
            var reconciled = unknown.Count == 0;

            var exposure = MetricContract.Compute(
                urn,
                request.AsOf,
                buckets.Gross,
                buckets.Credits,
                buckets.Rebates,
                buckets.Returns,
                buckets.Clearing,
                buckets.Disputed,
                lines.Select(l => l.Lineage).ToList(),
                reconciled);

            _logger.LogInformation(
                "Tool {Tool} dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} reconciled {Reconciled} durationMs {DurationMs}",
                Name, request.DealerUrn, request.CycleId, request.CorrelationId, reconciled,
                (DateTimeOffset.UtcNow - started).TotalMilliseconds);

            return new ComputeNetExposureResult(exposure, lines.Count, dealer.UnderInsolvencyMoratorium);
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(Name, "Failed to load dealer ledger facts.", ex);
        }
    }

    private static (Money Gross, Money Credits, Money Rebates, Money Returns, Money Clearing, Money Disputed)
        Classify(IReadOnlyList<LedgerPosition> lines)
    {
        Money gross = Money.Zero, credits = Money.Zero, rebates = Money.Zero, returns = Money.Zero, clearing = Money.Zero, disputed = Money.Zero;
        foreach (var line in lines)
        {
            switch (ClassifyOne(line.DocumentType))
            {
                case "gross": gross += line.Amount; break;
                case "credit": credits += Abs(line.Amount); break;
                case "rebate": rebates += Abs(line.Amount); break;
                case "return": returns += Abs(line.Amount); break;
                case "clearing": clearing += Abs(line.Amount); break;
                case "dispute": disputed += Abs(line.Amount); break;
            }
        }
        return (gross, credits, rebates, returns, clearing, disputed);
    }

    private static string? ClassifyOne(string documentType)
    {
        var t = documentType.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        if (Contains(t, "Invoice", "AR", "Gross", "Receivable")) return "gross";
        if (Contains(t, "CreditNote", "Credit")) return "credit";
        if (Contains(t, "Rebate", "Scheme")) return "rebate";
        if (Contains(t, "Return")) return "return";
        if (Contains(t, "Clearing")) return "clearing";
        if (Contains(t, "Dispute")) return "dispute";
        return null;
    }

    private static bool Contains(string value, params string[] tokens)
        => tokens.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static Money Abs(Money money) => new(Math.Abs(money.Amount), money.Currency);
}
