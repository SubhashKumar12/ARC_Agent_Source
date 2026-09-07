using ARC.Domain.Enums;
using ARC.Domain.Metrics;

namespace ARC.Domain.Rules;

/// <summary>R1a — Hold notice if position is unreconciled.</summary>
public sealed class R1aUnreconciledRule : IRule
{
    public string Id => "R1a";
    public string Version { get; }
    public RuleSet Set => RuleSet.NoticeEligibility;
    public bool IsBlocking => true;

    public R1aUnreconciledRule(string version) => Version = version;

    public RuleResult Evaluate(RuleContext context)
    {
        var exposure = RuleGuard.Require(context.Exposure, Id, "A1 ExposureBreakdown");
        var passed = exposure.Status == ReconciliationStatus.Reconciled;
        return new RuleResult(
            Id,
            Version,
            passed,
            IsBlocking,
            passed ? "Position reconciled." : "Position UNRECONCILED — exclude from notice eligibility; raise to Finance.");
    }
}
