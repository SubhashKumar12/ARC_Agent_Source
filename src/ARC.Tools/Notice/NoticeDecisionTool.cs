using Microsoft.Extensions.Logging;
using ARC.Domain.Entities;
using ARC.Domain.Exceptions;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;

namespace ARC.Tools.Notice;

public sealed record NoticeDecisionRequest(
    Dealer Dealer,
    ExposureBreakdown Exposure,
    DateOnly AsOf,
    Dispute? OpenDispute,
    PromiseToPay? ActivePromiseToPay,
    IReadOnlyList<Citation>? Citations,
    string? CorrelationId);

/// <summary>R1a/R1b/R1c/R5/R6 via RuleEngine. Does not approve G1.</summary>
public sealed class NoticeDecisionTool
{
    public const string Name = "DecideNotice";

    private readonly RuleEngine _rules;
    private readonly RuleConfiguration _configuration;
    private readonly ILogger<NoticeDecisionTool> _logger;

    public NoticeDecisionTool(RuleEngine rules, RuleConfiguration configuration, ILogger<NoticeDecisionTool> logger)
    {
        _rules = rules;
        _configuration = configuration;
        _logger = logger;
    }

    public NoticeVerdict Decide(NoticeDecisionRequest request)
    {
        var context = new RuleContext
        {
            Exposure = request.Exposure,
            Dealer = request.Dealer,
            OpenDispute = request.OpenDispute,
            ActivePromiseToPay = request.ActivePromiseToPay is { ConfirmedByTsi: true } confirmed
                ? confirmed
                : null,
            Configuration = _configuration,
            AsOf = request.AsOf
        };
        NoticeVerdict verdict;
        try
        {
            verdict = _rules.DecideNotice(context);
        }
        catch (MissingRulePrerequisiteException ex)
        {
            throw new ToolException(Name, ex.Message, ex);
        }

        if (request.Citations is { Count: > 0 })
            verdict = verdict with { Citations = request.Citations };

        _logger.LogInformation(
            "Tool {Tool} dealer {DealerUrn} correlation {CorrelationId} decision {Decision} requiresGate {Gate}",
            Name, request.Dealer.Urn.Value, request.CorrelationId, verdict.Decision, verdict.RequiresDepotManagerGate);
        return verdict;
    }
}
