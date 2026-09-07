using ARC.Agents.Workflows.Models;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;

namespace ARC.Api.DTOs;

public sealed record GateDecisionRequest
{
    public required string CycleId { get; init; }
    public required string DealerUrn { get; init; }
    public ArcWorkflowKind? Kind { get; init; }
    public required GateDecisionStatus Decision { get; init; }
    public required string Reason { get; init; }
}

public sealed record StartRunRequest
{
    public required ArcWorkflowKind Kind { get; init; }
    public DateOnly? AsOf { get; init; }
    public RunMode? Mode { get; init; }
}

public sealed record ConfirmPtpRequest
{
    public required string RecordId { get; init; }
    public DateOnly? CommitmentDate { get; init; }
    public decimal? Amount { get; init; }
    public DateOnly? AsOf { get; init; }
}

public sealed record NlqRequest
{
    public required string CycleId { get; init; }
    public required string Question { get; init; }
    public string? DealerUrn { get; init; }
    public string? Region { get; init; }
}

public sealed record CycleDashboardDto(
    string CycleId,
    int CaseCount,
    IReadOnlyDictionary<string, int> ByStatus,
    IReadOnlyDictionary<string, int> Waiting);

public sealed record CaseSummaryDto(
    string CycleId,
    string DealerUrn,
    string Status,
    string? WaitingGate,
    string CorrelationId,
    DateTimeOffset UpdatedUtc);

public sealed record CaseDetailDto(
    string CycleId,
    string DealerUrn,
    string Status,
    string? WaitingGate,
    string? TerminationReason,
    string? NoticeDecision,
    bool? Section138Eligible,
    string? ClockStatus,
    int? ClockDaysRemaining,
    IReadOnlyList<GateAuditDto> Gates,
    string? SelectedChequeNumber = null,
    string? SelectedChequeStatus = null,
    string? MemoReasonCode = null,
    DateOnly? NoticeByDate = null,
    DateOnly? CureEndsDate = null,
    DateOnly? FileByDate = null,
    string? A4ProvenanceStatus = null,
    IReadOnlyList<A4FactStatus>? A4DataCompleteness = null,
    IReadOnlyList<A4TbcIndicator>? A4TbcIndicators = null,
    bool ProductionLegalBlockedOnApprovedStoredProcedure = true);

public sealed record GateAuditDto(
    string Gate,
    string ActorUpn,
    string ActorRole,
    string Decision,
    string Reason,
    DateTimeOffset DecidedUtc);

public sealed record WorklistEntryDto(
    int Rank,
    string DealerUrn,
    decimal RecoverabilityScore,
    string RecoveryTier,
    string Status,
    string? WaitingGate,
    string ScoreFormula = RecoverabilityScoreFormula.Id,
    bool? IsTopDecile = null,
    string? DeterministicExplanation = null,
    string ProvenanceStatus = "Incomplete",
    IReadOnlyList<A2InputStatus>? DataCompleteness = null,
    IReadOnlyList<A2TbcIndicator>? TbcIndicators = null);

public sealed record RankedWorklistDto(
    string CycleId,
    bool TopDecile,
    int EligibleCount,
    int ReturnedCount,
    IReadOnlyList<WorklistEntryDto> Items,
    string ScoreFormula = RecoverabilityScoreFormula.Id,
    bool ProductionChatWorklistBlockedOnApprovedStoredProcedure = true,
    IReadOnlyList<A2TbcIndicator>? TbcIndicators = null);
