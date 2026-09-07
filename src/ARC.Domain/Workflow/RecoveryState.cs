using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Limitation;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Workflow;

/// <summary>
/// Per-dealer run state. MAF-agnostic. Checkpoint payload is this object.
/// Idempotency: (CycleId, DealerUrn, node).
/// </summary>
public sealed class RecoveryState
{
    public string SchemaVersion { get; init; } = "1.0";
    public required CycleId CycleId { get; init; }
    public required DealerUrn DealerUrn { get; init; }
    public required DateOnly AsOf { get; init; }
    public required CorrelationId CorrelationId { get; init; }
    public required RunMode Mode { get; init; }
    public WorkflowStatus Status { get; init; } = WorkflowStatus.Running;
    public ExposureBreakdown? Exposure { get; init; }
    public RiskAssessment? Risk { get; init; }
    public NoticeVerdict? NoticeVerdict { get; init; }
    public EligibilityVerdict? Eligibility { get; init; }
    public LimitationClock? Clock { get; init; }
    public string? SelectedChequeNumber { get; init; }
    public string? SelectedChequeStatus { get; init; }
    public string? MemoReasonCode { get; init; }
    public DateOnly? MemoIssueDate { get; init; }
    public DateOnly? MemoReceivedDate { get; init; }
    public IReadOnlyList<GateDecision> Approvals { get; init; } = [];
    public string? TerminationReason { get; init; }
    public GateId? WaitingGate { get; init; }

    public RecoveryState WithStatus(WorkflowStatus status, string? terminationReason = null)
        => Clone(status: status, terminationReason: terminationReason ?? TerminationReason);

    public RecoveryState WaitingFor(GateId gate)
        => Clone(status: WorkflowStatus.WaitingForHuman, waitingGate: gate);

    public RecoveryState WithExposure(ExposureBreakdown exposure)
        => Clone(exposure: exposure);

    public RecoveryState WithRisk(RiskAssessment risk)
        => Clone(risk: risk);

    public RecoveryState WithNotice(NoticeVerdict verdict)
        => Clone(notice: verdict);

    public RecoveryState WithEligibility(
        EligibilityVerdict eligibility,
        LimitationClock? clock,
        string? selectedChequeNumber = null,
        string? selectedChequeStatus = null,
        string? memoReasonCode = null,
        DateOnly? memoIssueDate = null,
        DateOnly? memoReceivedDate = null)
        => Clone(
            eligibility: eligibility,
            clock: clock,
            selectedChequeNumber: selectedChequeNumber,
            selectedChequeStatus: selectedChequeStatus,
            memoReasonCode: memoReasonCode,
            memoIssueDate: memoIssueDate,
            memoReceivedDate: memoReceivedDate);

    public RecoveryState WithApproval(GateDecision decision)
    {
        var approvals = Approvals.ToList();
        approvals.Add(decision);
        var progressed = decision.AllowsProgression;
        return Clone(
            status: progressed ? WorkflowStatus.Running : WorkflowStatus.Blocked,
            terminationReason: progressed ? null : $"{decision.Gate}:{decision.Decision}",
            approvals: approvals,
            clearWaitingGate: true,
            clearTerminationReason: progressed);
    }

    private RecoveryState Clone(
        WorkflowStatus? status = null,
        string? terminationReason = null,
        GateId? waitingGate = null,
        bool clearWaitingGate = false,
        bool clearTerminationReason = false,
        ExposureBreakdown? exposure = null,
        RiskAssessment? risk = null,
        NoticeVerdict? notice = null,
        EligibilityVerdict? eligibility = null,
        LimitationClock? clock = null,
        IReadOnlyList<GateDecision>? approvals = null,
        string? selectedChequeNumber = null,
        string? selectedChequeStatus = null,
        string? memoReasonCode = null,
        DateOnly? memoIssueDate = null,
        DateOnly? memoReceivedDate = null)
        => new()
        {
            SchemaVersion = SchemaVersion,
            CycleId = CycleId,
            DealerUrn = DealerUrn,
            AsOf = AsOf,
            CorrelationId = CorrelationId,
            Mode = Mode,
            Status = status ?? Status,
            Exposure = exposure ?? Exposure,
            Risk = risk ?? Risk,
            NoticeVerdict = notice ?? NoticeVerdict,
            Eligibility = eligibility ?? Eligibility,
            Clock = clock ?? Clock,
            SelectedChequeNumber = selectedChequeNumber ?? SelectedChequeNumber,
            SelectedChequeStatus = selectedChequeStatus ?? SelectedChequeStatus,
            MemoReasonCode = memoReasonCode ?? MemoReasonCode,
            MemoIssueDate = memoIssueDate ?? MemoIssueDate,
            MemoReceivedDate = memoReceivedDate ?? MemoReceivedDate,
            Approvals = approvals ?? Approvals,
            TerminationReason = clearTerminationReason ? null : terminationReason ?? TerminationReason,
            WaitingGate = clearWaitingGate ? null : waitingGate ?? WaitingGate
        };
}

public sealed record WorkflowCheckpoint
{
    public CycleId CycleId { get; }
    public DealerUrn DealerUrn { get; }
    public string Node { get; }
    public WorkflowStatus Status { get; }
    public DateTimeOffset CapturedUtc { get; }

    public WorkflowCheckpoint(
        CycleId cycleId,
        DealerUrn dealerUrn,
        string node,
        WorkflowStatus status,
        DateTimeOffset capturedUtc)
    {
        if (string.IsNullOrWhiteSpace(node))
            throw new ArgumentException("Workflow node is required for idempotency.", nameof(node));

        CycleId = cycleId;
        DealerUrn = dealerUrn;
        Node = node;
        Status = status;
        CapturedUtc = capturedUtc;
    }

    public string IdempotencyKey => $"{CycleId.Value}|{DealerUrn.Value}|{Node}";
}
