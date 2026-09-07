using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;
using ARC.Tools.Persistence;

namespace ARC.Tools.Field;

/// <summary>
/// Deterministic broken-PTP chase scanner.
/// Production prefers ListDueConfirmedAsync; still verifies ConfirmedByTsi and AsOf &gt; CommitmentDate in code.
/// </summary>
public sealed class PtpChaseScanner
{
    public const string Name = "ScanPtpChase";

    private readonly IPtpRepository _ptps;
    private readonly IPtpChaseStore _chases;
    private readonly IDealerRepository _dealers;
    private readonly ILogger<PtpChaseScanner> _logger;

    public PtpChaseScanner(
        IPtpRepository ptps,
        IPtpChaseStore chases,
        IDealerRepository dealers,
        ILogger<PtpChaseScanner> logger)
    {
        _ptps = ptps;
        _chases = chases;
        _dealers = dealers;
        _logger = logger;
    }

    /// <summary>Production/Timer path: due confirmed PTPs across cycles.</summary>
    public async Task<IReadOnlyList<PtpChase>> ScanDueAsync(
        DateOnly asOf,
        RunMode mode,
        CorrelationId correlationId,
        CancellationToken cancellationToken)
    {
        using var activity = new ActivitySource("ARC").StartActivity("arc.ptp.chase.scan");
        activity?.SetTag("correlation_id", correlationId.Value);
        activity?.SetTag("mode", mode.ToString());

        var due = await _ptps.ListDueConfirmedAsync(asOf, cancellationToken);
        var created = await ProcessCandidatesAsync(due, asOf, mode, correlationId, activity, cancellationToken);
        activity?.SetTag("row_count", created.Count);
        activity?.SetTag("status", "success");
        return created;
    }

    /// <summary>Cycle-scoped scan (CLI/tests). Still applies domain broken rule in code.</summary>
    public async Task<IReadOnlyList<PtpChase>> ScanCycleAsync(
        CycleId cycleId,
        DateOnly asOf,
        RunMode mode,
        CorrelationId correlationId,
        CancellationToken cancellationToken)
    {
        using var activity = new ActivitySource("ARC").StartActivity("arc.ptp.chase");
        activity?.SetTag("cycle_id", cycleId.Value);
        activity?.SetTag("correlation_id", correlationId.Value);
        activity?.SetTag("mode", mode.ToString());

        var started = DateTimeOffset.UtcNow;
        try
        {
            var candidates = await _ptps.ListCommittedAsync(cycleId, cancellationToken);
            var created = await ProcessCandidatesAsync(candidates, asOf, mode, correlationId, activity, cancellationToken);
            activity?.SetTag("status", "success");
            activity?.SetTag("committed_count", candidates.Count);
            activity?.SetTag("new_chase_count", created.Count);
            _logger.LogInformation(
                "PTP chase scanner cycle {CycleId} completed: {NewChases} new chases, durationMs {DurationMs}",
                cycleId.Value, created.Count, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
            return created;
        }
        catch (DataAccessException ex)
        {
            activity?.SetTag("status", "error");
            _logger.LogError(ex, "PTP chase scanner failed for cycle {CycleId}", cycleId.Value);
            throw new ToolException(Name, "Failed to scan PTP chase.", ex);
        }
    }

    private async Task<IReadOnlyList<PtpChase>> ProcessCandidatesAsync(
        IReadOnlyList<PtpCandidateRecord> candidates,
        DateOnly asOf,
        RunMode mode,
        CorrelationId correlationId,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var newChases = new List<PtpChase>();

        foreach (var candidate in candidates)
        {
            // Authoritative domain rules (SQL filter is optimization only).
            if (candidate.Committed is not { ConfirmedByTsi: true } ptp)
                continue;
            if (asOf <= ptp.CommitmentDate)
                continue;

            var dealer = await _dealers.GetAsync(ptp.DealerUrn, cancellationToken);
            if (dealer is null)
            {
                _logger.LogWarning(
                    "PTP chase scanner: dealer {DealerUrn} not found for PTP {PtpId}",
                    ptp.DealerUrn.Value, candidate.RecordId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(dealer.CoveringTsi))
            {
                _logger.LogWarning(
                    "PTP chase scanner: dealer {DealerUrn} missing CoveringTsi - fail closed, no chase created",
                    ptp.DealerUrn.Value);
                continue;
            }

            var chaseId = $"{candidate.RecordId}|Broken|{ptp.CommitmentDate:yyyyMMdd}";
            var status = mode == RunMode.Shadow ? PtpChaseStatus.SuppressedShadow : PtpChaseStatus.Open;

            var chase = new PtpChase
            {
                ChaseId = chaseId,
                PtpId = candidate.RecordId,
                CycleId = new CycleId(candidate.CycleId),
                DealerUrn = ptp.DealerUrn,
                OwnerTsi = dealer.CoveringTsi,
                CommitmentDate = ptp.CommitmentDate,
                DueDate = ptp.CommitmentDate,
                ChaseType = PtpChaseType.Broken,
                Status = status,
                CorrelationId = correlationId
            };

            var inserted = await _chases.TryInsertAsync(chase, cancellationToken);
            if (!inserted)
                continue;

            newChases.Add(chase);
            _logger.LogInformation(
                "PTP chase created {ChaseId} dealer {DealerUrn} owner {OwnerTsi} status {Status}",
                chaseId, ptp.DealerUrn.Value, dealer.CoveringTsi, status);
        }

        return newChases;
    }
}
