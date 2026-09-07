using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Workflow;
using ARC.Tools.Evidence;
using ARC.Tools.Field;

namespace ARC.Agents.Workflows.Models;

public enum ArcWorkflowKind
{
    Odos = 0,
    Section138 = 1
}

/// <summary>Host start payload for Workflow A or B. Not a financial fact.</summary>
public sealed record WorkflowRunRequest
{
    public required string CycleId { get; init; }
    public required string DealerUrn { get; init; }
    public required DateOnly AsOf { get; init; }
    public required string CorrelationId { get; init; }
    public required RunMode Mode { get; init; }
    public required ArcWorkflowKind Kind { get; init; }
    public DemandNotice? DemandNotice { get; init; }
    public Dispute? OpenDispute { get; init; }
    public PromiseToPay? ActivePromiseToPay { get; init; }
    public string? TsiRemarks { get; init; }
    public string? SearchText { get; init; }
    public IReadOnlyList<EvidenceItem> Evidence { get; init; } = [];
}

/// <summary>Typed message flowing through MAF edges. Agents still go through ARC.Tools.</summary>
public sealed record WorkflowMessage
{
    public required RecoveryState State { get; init; }
    public required ArcWorkflowKind Kind { get; init; }
    public Dealer? Dealer { get; init; }
    public DemandNotice? DemandNotice { get; init; }
    public Dispute? OpenDispute { get; init; }
    public PromiseToPay? ActivePromiseToPay { get; init; }
    public SecurityCheque? Cheque { get; init; }
    public ChequeReturnMemo? Memo { get; init; }
    public string? TsiRemarks { get; init; }
    public string? SearchText { get; init; }
    public IReadOnlyList<EvidenceItem> Evidence { get; init; } = [];
    public VisitTask? Visit { get; init; }
    public string? Explanation { get; init; }
}

public sealed record GateApprovalResponse(
    string ActorUpn,
    ActorRole ActorRole,
    GateDecisionStatus Decision,
    string Reason,
    string? CycleId = null,
    string? DealerUrn = null);

/// <summary>Human gate resume payload shared by ARC.Api and ARC.Host.Functions.</summary>
public sealed record GateResumeRequest
{
    public required string CycleId { get; init; }
    public required string DealerUrn { get; init; }
    public required ArcWorkflowKind Kind { get; init; }
    public required string ActorUpn { get; init; }
    public required ActorRole ActorRole { get; init; }
    public required GateDecisionStatus Decision { get; init; }
    public required string Reason { get; init; }
}

public sealed record PendingGateHalt(
    string SessionId,
    string CheckpointId,
    string RequestId,
    string PortId,
    ArcWorkflowKind Kind);
