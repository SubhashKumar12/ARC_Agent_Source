using ARC.Domain.ValueObjects;

namespace ARC.Tools.Field;

/// <summary>
/// PTP chase task for broken promises (AsOf > CommitmentDate).
/// Only confirmed PTPs chase. Unconfirmed candidates do not affect R1c or chase.
/// </summary>
public sealed class PtpChase
{
    public required string ChaseId { get; init; }
    public required string PtpId { get; init; }
    public required CycleId CycleId { get; init; }
    public required DealerUrn DealerUrn { get; init; }
    public required string OwnerTsi { get; init; }
    public required DateOnly CommitmentDate { get; init; }
    public required DateOnly DueDate { get; init; }
    public required PtpChaseType ChaseType { get; init; }
    public required PtpChaseStatus Status { get; init; }
    public required CorrelationId CorrelationId { get; init; }
}

public enum PtpChaseType
{
    Broken = 0
}

public enum PtpChaseStatus
{
    Open = 0,
    SuppressedShadow = 1
}
