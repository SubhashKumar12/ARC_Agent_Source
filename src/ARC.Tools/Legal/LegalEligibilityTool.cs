using Microsoft.Extensions.Logging;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Exceptions;
using ARC.Domain.Limitation;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;

namespace ARC.Tools.Legal;

public sealed record LegalEligibilityRequest(
    string DealerUrn,
    DateOnly AsOf,
    ExposureBreakdown Exposure,
    DemandNotice? DemandNotice,
    string? CycleId,
    string? CorrelationId);

public sealed record LegalEligibilityResult(
    EligibilityVerdict Eligibility,
    LimitationClock? Clock,
    IReadOnlyList<ClockAlert> Alerts,
    SecurityCheque? SelectedCheque,
    ChequeReturnMemo? Memo = null);

/// <summary>CheckSection138Eligibility + GetLimitationClock. Does not approve G3.</summary>
public sealed class LegalEligibilityTool
{
    public const string Name = "CheckSection138Eligibility";

    private readonly IDealerRepository _dealers;
    private readonly IChequeRepository _cheques;
    private readonly ILimitationClockService _clocks;
    private readonly RuleEngine _rules;
    private readonly RuleConfiguration _configuration;
    private readonly ILogger<LegalEligibilityTool> _logger;

    public LegalEligibilityTool(
        IDealerRepository dealers,
        IChequeRepository cheques,
        ILimitationClockService clocks,
        RuleEngine rules,
        RuleConfiguration configuration,
        ILogger<LegalEligibilityTool> logger)
    {
        _dealers = dealers;
        _cheques = cheques;
        _clocks = clocks;
        _rules = rules;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<LegalEligibilityResult> CheckSection138EligibilityAsync(
        LegalEligibilityRequest request,
        CancellationToken cancellationToken)
        => EvaluateAsync(request, cancellationToken);

    public async Task<LegalEligibilityResult> EvaluateAsync(LegalEligibilityRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var (dealer, cheque, memo, clock, alerts) = await LoadFactsAsync(
                request.DealerUrn, request.AsOf, request.DemandNotice, cancellationToken);

            EligibilityVerdict eligibility;
            try
            {
                var context = new RuleContext
                {
                    Exposure = request.Exposure,
                    Dealer = dealer,
                    Cheque = cheque,
                    ReturnMemo = memo,
                    DemandNotice = request.DemandNotice,
                    Clock = clock,
                    Configuration = _configuration,
                    AsOf = request.AsOf
                };
                eligibility = _rules.DecideSection138(context);
            }
            catch (MissingRulePrerequisiteException ex)
            {
                eligibility = new EligibilityVerdict(false, [], ex.Message);
            }

            _logger.LogInformation(
                "Tool {Tool} dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} eligible {Eligible} clock {Clock} durationMs {DurationMs}",
                Name, request.DealerUrn, request.CycleId, request.CorrelationId, eligibility.Eligible, clock?.Status,
                (DateTimeOffset.UtcNow - started).TotalMilliseconds);

            return new LegalEligibilityResult(eligibility, clock, alerts, cheque, memo);
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(Name, "Failed to load dealer or cheque facts.", ex);
        }
    }

    public const string ClockToolName = "GetLimitationClock";

    public async Task<LimitationClockResult> GetLimitationClockAsync(
        GetLimitationClockRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var (_, cheque, memo, clock, alerts) = await LoadFactsAsync(
                request.DealerUrn, request.AsOf, request.DemandNotice, cancellationToken);
            if (memo is null || clock is null)
                throw new ToolException(ClockToolName, "Cheque return memo was not found — limitation clock cannot be computed.");

            _logger.LogInformation(
                "Tool {Tool} dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} status {Status} durationMs {DurationMs}",
                ClockToolName, request.DealerUrn, request.CycleId, request.CorrelationId, clock.Status,
                (DateTimeOffset.UtcNow - started).TotalMilliseconds);

            return new LimitationClockResult(clock, alerts, cheque, memo);
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(ClockToolName, "Failed to load cheque return memo for the limitation clock.", ex);
        }
    }

    private async Task<(Dealer Dealer, SecurityCheque? Cheque, ChequeReturnMemo? Memo, LimitationClock? Clock, IReadOnlyList<ClockAlert> Alerts)>
        LoadFactsAsync(string dealerUrn, DateOnly asOf, DemandNotice? demandNotice, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dealerUrn))
            throw new ToolException(Name, "DealerUrn is required.");

        var urn = new DealerUrn(dealerUrn);
        var dealer = await _dealers.GetAsync(urn, cancellationToken)
            ?? throw new ToolException(Name, $"Dealer '{dealerUrn}' was not found.");

        var cheques = await _cheques.ListChequesAsync(urn, cancellationToken);
        var memos = await _cheques.ListReturnMemosAsync(urn, cancellationToken);
        var cheque = ChequeSelection.Select(cheques, memos);
        var memo = cheque is null
            ? null
            : memos.FirstOrDefault(m => string.Equals(m.ChequeNumber, cheque.ChequeNumber, StringComparison.OrdinalIgnoreCase));

        LimitationClock? clock = null;
        IReadOnlyList<ClockAlert> alerts = [];
        if (memo is not null)
        {
            clock = _clocks.Compute(memo, demandNotice, asOf, _configuration);
            alerts = _clocks.DueAlerts(clock, asOf);
        }

        return (dealer, cheque, memo, clock, alerts);
    }
}

public sealed record GetLimitationClockRequest(
    string DealerUrn,
    DateOnly AsOf,
    DemandNotice? DemandNotice,
    string? CycleId,
    string? CorrelationId);

public sealed record LimitationClockResult(
    LimitationClock Clock,
    IReadOnlyList<ClockAlert> Alerts,
    SecurityCheque? SelectedCheque,
    ChequeReturnMemo Memo);
