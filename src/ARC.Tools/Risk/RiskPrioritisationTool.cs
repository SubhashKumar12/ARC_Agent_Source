using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Tools.Models;

namespace ARC.Tools.Risk;

public sealed record RiskPrioritisationRequest(
    ExposureBreakdown Exposure,
    bool HasBouncedSecurityCheque,
    int? DaysSinceDemandNotice,
    string? CorrelationId);

/// <summary>
/// Ranking uses net recoverable exposure (source: prioritise by recoverability/value).
/// Exact recoverability_score formula is To Be Confirmed — not invented as a model.
/// Section138 tier uses Process B trigger: bounced cheque + 60 days non-payment.
/// </summary>
public sealed class RiskPrioritisationTool
{
    public const string Name = "PrioritiseRecovery";

    private readonly ArcToolsOptions _options;
    private readonly ILogger<RiskPrioritisationTool> _logger;

    public RiskPrioritisationTool(IOptions<ArcToolsOptions> options, ILogger<RiskPrioritisationTool> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>True when a runtime visit cutoff is configured. Assignment confirmation of that cutoff remains TBC.</summary>
    public bool VisitCutoffIsConfigured => _options.VisitMaxNetExposure is not null;

    public RiskAssessment Prioritise(RiskPrioritisationRequest request)
    {
        var net = request.Exposure.NetRecoverableExposure.Amount;
        var score = net;

        RecoveryTier tier;
        if (request.HasBouncedSecurityCheque
            && request.DaysSinceDemandNotice >= _options.Section138NonPaymentDays)
        {
            tier = RecoveryTier.Section138;
        }
        else if (_options.VisitMaxNetExposure is { } visitMax && net <= visitMax)
        {
            tier = RecoveryTier.Visit;
        }
        else
        {
            tier = RecoveryTier.Notice;
        }

        var assessment = new RiskAssessment(tier, score);
        _logger.LogInformation(
            "Tool {Tool} dealer {DealerUrn} correlation {CorrelationId} tier {Tier} score {Score}",
            Name, request.Exposure.DealerUrn.Value, request.CorrelationId, tier, score);
        return assessment;
    }
}
