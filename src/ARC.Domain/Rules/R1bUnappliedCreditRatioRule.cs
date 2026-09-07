using ARC.Domain.Enums;

namespace ARC.Domain.Rules;

/// <summary>
/// R1b — Hold if unapplied_credits / claim_amount &gt; threshold (source 0.40).
/// Denominator: source rule text says claim_amount; sample config uses GrossOpenAr.
/// This implementation uses GrossOpenAr as claim_amount pending Finance confirmation.
/// </summary>
public sealed class R1bUnappliedCreditRatioRule : IRule
{
    public string Id => "R1b";
    public string Version { get; }
    public RuleSet Set => RuleSet.NoticeEligibility;
    public bool IsBlocking => true;

    public R1bUnappliedCreditRatioRule(string version) => Version = version;

    public RuleResult Evaluate(RuleContext context)
    {
        var exposure = RuleGuard.Require(context.Exposure, Id, "A1 ExposureBreakdown");
        if (exposure.GrossOpenAr.Amount == 0m)
            throw new Exceptions.MissingRulePrerequisiteException(Id, "non-zero claim_amount / GrossOpenAr");

        var ratio = exposure.UnappliedCreditRatio;
        var threshold = context.Configuration.UnappliedCreditRatioThreshold;
        var passed = ratio <= threshold;
        return new RuleResult(
            Id,
            Version,
            passed,
            IsBlocking,
            passed
                ? $"Unapplied credit ratio {ratio:P1} ≤ {threshold:P0}."
                : $"Unapplied credit ratio {ratio:P1} > {threshold:P0} — Reconcile; route to Finance.");
    }
}
