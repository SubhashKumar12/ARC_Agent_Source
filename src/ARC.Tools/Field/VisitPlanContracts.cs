using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Tools.Field;

/// <summary>
/// Deterministic geo-clustered visit plan for a covering TSI.
/// Geo = CoveringTsi + Region + Depot (no lat/long, no Maps).
/// </summary>
public sealed class VisitPlan
{
    public required string PlanId { get; init; }
    public required CycleId CycleId { get; init; }
    public required string TsiId { get; init; }
    public required DateOnly PlanDate { get; init; }
    public required CorrelationId CorrelationId { get; init; }
    public required IReadOnlyList<VisitPlanLine> Lines { get; init; }
}

/// <summary>
/// One dealer visit in a TSI plan, ordered by upstream A2 priority.
/// </summary>
public sealed class VisitPlanLine
{
    public required DealerUrn DealerUrn { get; init; }
    public required int Sequence { get; init; }
    public required int PriorityRank { get; init; }
    public required string GeoClusterId { get; init; }
    public required VisitPlanReason Reason { get; init; }
    public required VisitPlanStatus Status { get; init; }
    public required string VisitTaskId { get; init; }
}

/// <summary>
/// Bounded reason codes for why this dealer is in a visit plan.
/// No model-generated prose.
/// </summary>
public enum VisitPlanReason
{
    VisitTier = 0,
    PostNotice = 1,
    BrokenPtpFirst = 2
}

public enum VisitPlanStatus
{
    Planned = 0,
    SuppressedShadow = 1
}
