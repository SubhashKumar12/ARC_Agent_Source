using ARC.Domain.Enums;
using ARC.Domain.Metrics;

namespace ARC.Domain.Rules;

public sealed class RuleEngine
{
    private readonly IReadOnlyList<IRule> _rules;

    public RuleEngine(IEnumerable<IRule> rules) => _rules = [.. rules];

    public static RuleEngine CreateDefault(RuleConfiguration configuration)
    {
        var v = configuration.Version;
        return new RuleEngine(
        [
            new R1aUnreconciledRule(v),
            new R1bUnappliedCreditRatioRule(v),
            new R1cDisputeOrPtpHoldRule(v),
            new R2Section138EligibilityRule(v),
            new R5MoratoriumRule(v),
            new R6LineageRule(v)
        ]);
    }

    public IReadOnlyList<RuleResult> Evaluate(RuleSet set, RuleContext context)
        => _rules.Where(r => r.Set == set || r.Set == RuleSet.AllActions)
            .Select(r => r.Evaluate(context))
            .ToList();

    public NoticeVerdict DecideNotice(RuleContext context)
    {
        var results = Evaluate(RuleSet.NoticeEligibility, context);
        var blocking = results.Where(r => r.Blocks).Select(r => r.RuleId).ToHashSet(StringComparer.Ordinal);

        NoticeDecision decision;
        if (blocking.Contains("R5") || blocking.Contains("R1a") || blocking.Contains("R6"))
            decision = NoticeDecision.Hold;
        else if (blocking.Contains("R1b"))
            decision = NoticeDecision.Reconcile;
        else if (blocking.Contains("R1c"))
            decision = NoticeDecision.Hold;
        else
            decision = NoticeDecision.Issue;

        return new NoticeVerdict(decision, results, []);
    }

    public EligibilityVerdict DecideSection138(RuleContext context)
    {
        var results = Evaluate(RuleSet.Section138Eligibility, context);
        var blocked = results.FirstOrDefault(r => r.Blocks);
        return blocked is null
            ? new EligibilityVerdict(true, results)
            : new EligibilityVerdict(false, results, blocked.Message);
    }
}
