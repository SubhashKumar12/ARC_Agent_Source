using ARC.Domain.Enums;

namespace ARC.Domain.Rules;

/// <summary>R6 — No notice may quote an amount lacking a resolvable lineage chain to source rows.</summary>
public sealed class R6LineageRule : IRule
{
    public string Id => "R6";
    public string Version { get; }
    public RuleSet Set => RuleSet.NoticeEligibility;
    public bool IsBlocking => true;

    public R6LineageRule(string version) => Version = version;

    public RuleResult Evaluate(RuleContext context)
    {
        var exposure = RuleGuard.Require(context.Exposure, Id, "A1 ExposureBreakdown");
        var passed = exposure.Lineage.Count > 0;
        return new RuleResult(
            Id,
            Version,
            passed,
            IsBlocking,
            passed
                ? $"Lineage present ({exposure.Lineage.Count} source row(s))."
                : "No resolvable lineage chain — notice must not quote an amount.");
    }
}
