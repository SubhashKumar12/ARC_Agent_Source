using ARC.Data.Odos;
using ARC.Domain.ValueObjects;
using ARC.Tools.Field;
using ARC.Tools.Persistence;
using Microsoft.Extensions.Logging;

namespace ARC.Tools.Field;

/// <summary>
/// Composed SP7 field-recovery read for Business Chat. No SQL in this class.
/// </summary>
public sealed class GetFieldRecoveryDetailsTool
{
    public const string Name = "getFieldRecoveryDetails";

    private readonly IPtpRepository _ptp;
    private readonly IPtpChaseStore _chases;
    private readonly ILogger<GetFieldRecoveryDetailsTool> _logger;

    public GetFieldRecoveryDetailsTool(
        IPtpRepository ptp,
        IPtpChaseStore chases,
        ILogger<GetFieldRecoveryDetailsTool> logger)
    {
        _ptp = ptp;
        _chases = chases;
        _logger = logger;
    }

    public async Task<FieldRecoveryChatFacts?> GetByDealerUrnAsync(
        string dealerUrn,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(dealerUrn))
            return null;

        var urn = new DealerUrn(dealerUrn);
        var ptps = await _ptp.ListCommittedByDealerAsync(urn, cancellationToken);
        var latestPtp = ptps
            .Where(p => p.Status == PtpCandidateStatus.Confirmed && !p.Discarded)
            .OrderByDescending(p => p.CommitmentDate)
            .FirstOrDefault();

        var openChases = await _chases.ListByStatusAsync(PtpChaseStatus.Open, cancellationToken);
        var dealerChase = openChases
            .Where(c => c.DealerUrn.Value.Equals(dealerUrn, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.DueDate)
            .FirstOrDefault();

        if (latestPtp is null && dealerChase is null)
            return null;

        _logger.LogInformation(
            "Tool {Tool} loaded field recovery for {DealerUrn}. Ptp={HasPtp} Chase={HasChase} CorrelationId={CorrelationId}",
            Name,
            dealerUrn,
            latestPtp is not null,
            dealerChase is not null,
            correlationId ?? "(none)");

        return new FieldRecoveryChatFacts(
            dealerUrn,
            latestPtp?.Amount,
            latestPtp?.CommitmentDate,
            latestPtp?.Status.ToString(),
            latestPtp?.SpeechConfidence,
            dealerChase?.Status.ToString(),
            LastVisitDate: null,
            VisitStatus: null,
            dealerChase?.OwnerTsi,
            VisitPlanStatus: null);
    }

    public static string UrnForOdosKeys(string depotCode, string dealerCode)
        => OdosChatDealerUrn.ForKeys(depotCode, dealerCode).Value;
}

public sealed record FieldRecoveryChatFacts(
    string DealerUrn,
    decimal? PtpAmount,
    DateOnly? PtpDate,
    string? PtpStatus,
    decimal? PtpConfidence,
    string? ChaseStatus,
    DateOnly? LastVisitDate,
    string? VisitStatus,
    string? VisitOwner,
    string? VisitPlanStatus);
