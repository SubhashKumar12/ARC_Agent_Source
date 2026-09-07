namespace ARC.Data.Sql;

/// <summary>
/// Data-owned PTP lifecycle row (candidate → committed). Maps to dbo.PtpRecord.
/// ARC.Data must not reference ARC.Tools types.
/// </summary>
public sealed class PtpRecordEntity
{
    public required string RecordId { get; init; }
    public required string CycleId { get; init; }
    public required string DealerUrn { get; init; }
    public DateOnly? CommitmentDate { get; init; }
    public decimal? Amount { get; init; }
    public string Currency { get; init; } = "INR";
    public required string Status { get; init; }
    public bool ConfirmedByTsi { get; init; }
    public DateTimeOffset? ConfirmedUtc { get; init; }
    public string? ConfirmedByUpn { get; init; }
    public bool RequiresTsiConfirmation { get; init; }
    public bool Discarded { get; init; }
    public required string Locale { get; init; }
    public decimal? SpeechConfidence { get; init; }
    public string? TranscriptSha256 { get; init; }
    public string? RecognitionStatus { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}

public sealed class VisitPlanEntity
{
    public required string PlanId { get; init; }
    public required string CycleId { get; init; }
    public required string TsiId { get; init; }
    public required DateOnly PlanDate { get; init; }
    public required string CorrelationId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public required IReadOnlyList<VisitPlanLineEntity> Lines { get; init; }
}

public sealed class VisitPlanLineEntity
{
    public required string PlanId { get; init; }
    public required string DealerUrn { get; init; }
    public required int Sequence { get; init; }
    public required int PriorityRank { get; init; }
    public required string GeoClusterId { get; init; }
    public required string Reason { get; init; }
    public required string Status { get; init; }
    public required string VisitTaskId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class PtpChaseEntity
{
    public required string ChaseId { get; init; }
    public required string PtpId { get; init; }
    public required string CycleId { get; init; }
    public required string DealerUrn { get; init; }
    public required string OwnerTsi { get; init; }
    public required DateOnly CommitmentDate { get; init; }
    public required DateOnly DueDate { get; init; }
    public required string ChaseType { get; init; }
    public required string Status { get; init; }
    public required string CorrelationId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public interface IPtpRecordRepository
{
    Task UpsertAsync(PtpRecordEntity record, CancellationToken cancellationToken);
    Task<PtpRecordEntity?> GetAsync(string recordId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PtpRecordEntity>> ListCommittedByCycleAsync(string cycleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PtpRecordEntity>> ListCommittedByDealerAsync(string dealerUrn, CancellationToken cancellationToken);
    Task<IReadOnlyList<PtpRecordEntity>> ListDueConfirmedAsync(DateOnly asOf, CancellationToken cancellationToken);
}

public interface IVisitPlanRepository
{
    Task UpsertAsync(VisitPlanEntity plan, CancellationToken cancellationToken);
    Task<VisitPlanEntity?> GetAsync(string planId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VisitPlanEntity>> ListByCycleAsync(string cycleId, CancellationToken cancellationToken);
}

public interface IPtpChaseRepository
{
    /// <summary>Atomic insert-if-absent. Returns true when a new row was inserted.</summary>
    Task<bool> TryInsertAsync(PtpChaseEntity chase, CancellationToken cancellationToken);
    Task<PtpChaseEntity?> GetAsync(string chaseId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PtpChaseEntity>> ListByCycleAsync(string cycleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PtpChaseEntity>> ListByStatusAsync(string status, CancellationToken cancellationToken);
}
