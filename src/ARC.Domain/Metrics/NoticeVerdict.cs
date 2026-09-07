using ARC.Domain.Enums;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Metrics;

public sealed record NoticeVerdict(
    NoticeDecision Decision,
    IReadOnlyList<RuleResult> RuleResults,
    IReadOnlyList<Citation> Citations,
    string? Justification = null)
{
    public bool RequiresDepotManagerGate => Decision == NoticeDecision.Issue;
}

public sealed record RiskAssessment(RecoveryTier Tier, decimal? Score = null);

public sealed record EligibilityVerdict(bool Eligible, IReadOnlyList<RuleResult> RuleResults, string? BlockReason = null);
