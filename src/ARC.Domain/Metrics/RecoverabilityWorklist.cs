using ARC.Domain.Enums;

namespace ARC.Domain.Metrics;

/// <summary>
/// SP3 ranking helpers. Score itself is A2's deterministic net recoverable exposure.
/// Tie-break and top-decile are defined here so SQL and in-memory stay aligned.
/// </summary>
public static class RecoverabilityWorklist
{
    public const decimal DecileFraction = 0.10m;

    /// <summary>CEILING(eligible * 0.10), minimum 1 when eligible &gt; 0.</summary>
    public static int TopDecileSize(int eligibleCount)
    {
        if (eligibleCount <= 0)
            return 0;
        return (int)Math.Ceiling(eligibleCount * DecileFraction);
    }

    public static bool IsExcludedStatus(string status)
        => string.Equals(status, nameof(WorkflowStatus.Blocked), StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, nameof(WorkflowStatus.Failed), StringComparison.OrdinalIgnoreCase);

    public static bool IsRankable(decimal? recoverabilityScore, string? recoveryTier, string status)
        => recoverabilityScore is not null
           && !string.IsNullOrWhiteSpace(recoveryTier)
           && !IsExcludedStatus(status);
}
