using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using ARC.Agents.A1Reconciliation;
using ARC.Agents.A2RiskPrioritisation;
using ARC.Agents.A3NoticeDecisioning;
using ARC.Agents.A4LegalEligibility;
using ARC.Agents.A5DraftingVerification;
using ARC.Agents.A6FieldOrchestration;
using ARC.Agents.A7EvidenceCaseFile;
using ARC.Agents.Context;
using ARC.Agents.Models;
using ARC.Agents.Observability;
using ARC.Agents.Workflows.Models;
using ARC.Agents.Workflows.Outbound;
using ARC.Agents.Workflows.Persistence;
using ARC.Data.Messaging;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;
using ARC.Knowledge.Documents;
using ARC.Tools.Drafting;

namespace ARC.Agents.Workflows.Executors;

/// <summary>MAF function executors. Each node calls an ARC agent/tool; none approve gates.</summary>
public sealed class ArcWorkflowExecutors
{
    private readonly ReconciliationAgent _a1;
    private readonly RiskPrioritisationAgent _a2;
    private readonly NoticeDecisioningAgent _a3;
    private readonly LegalEligibilityAgent _a4;
    private readonly DraftingVerificationAgent _a5;
    private readonly FieldOrchestrationAgent _a6;
    private readonly EvidenceCaseFileAgent _a7;
    private readonly IDealerRepository _dealers;
    private readonly IChequeRepository _cheques;
    private readonly IGateDecisionRepository _gates;
    private readonly IServiceBusPublisher _bus;
    private readonly IOutboundGate _outbound;
    private readonly WorkflowNodePersistence _persistence;
    private readonly IDocumentExtractionOrchestrator _documentExtraction;
    private readonly ILogger<ArcWorkflowExecutors> _logger;

    public ArcWorkflowExecutors(
        ReconciliationAgent a1,
        RiskPrioritisationAgent a2,
        NoticeDecisioningAgent a3,
        LegalEligibilityAgent a4,
        DraftingVerificationAgent a5,
        FieldOrchestrationAgent a6,
        EvidenceCaseFileAgent a7,
        IDealerRepository dealers,
        IChequeRepository cheques,
        IGateDecisionRepository gates,
        IServiceBusPublisher bus,
        IOutboundGate outbound,
        WorkflowNodePersistence persistence,
        IDocumentExtractionOrchestrator documentExtraction,
        ILogger<ArcWorkflowExecutors> logger)
    {
        _a1 = a1;
        _a2 = a2;
        _a3 = a3;
        _a4 = a4;
        _a5 = a5;
        _a6 = a6;
        _a7 = a7;
        _dealers = dealers;
        _cheques = cheques;
        _gates = gates;
        _bus = bus;
        _outbound = outbound;
        _persistence = persistence;
        _documentExtraction = documentExtraction;
        _logger = logger;
    }

    public async ValueTask<WorkflowMessage> A1Async(WorkflowRunRequest request, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var cycle = new CycleId(request.CycleId);
        var urn = new DealerUrn(request.DealerUrn);
        var state = new RecoveryState
        {
            CycleId = cycle,
            DealerUrn = urn,
            AsOf = request.AsOf,
            CorrelationId = new CorrelationId(request.CorrelationId),
            Mode = request.Mode
        };
        using var trace = TraceNode(ArcWorkflowNodes.A1, state);

        var dealer = await _dealers.GetAsync(urn, cancellationToken)
            ?? throw new InvalidOperationException($"Dealer '{request.DealerUrn}' was not found.");

        if (await TryResumeAsync(ArcWorkflowNodes.A1, state, request.Kind, dealer, request, context, cancellationToken) is { } resumed)
            return resumed;

        var result = await _a1.RunAsync(
            new ReconciliationAgentRequest(request.DealerUrn, ToAgentContext(state)),
            cancellationToken);

        state = state.WithExposure(result.Facts.Exposure);
        if (result.Facts.DealerUnderMoratorium)
            state = state.WithStatus(WorkflowStatus.Blocked, "R5 insolvency moratorium.");
        else if (result.Facts.Exposure.Status == ReconciliationStatus.Unreconciled)
            state = state.WithStatus(WorkflowStatus.Blocked, "R1a unreconciled — Finance.");

        var message = new WorkflowMessage
        {
            State = state,
            Kind = request.Kind,
            Dealer = dealer,
            DemandNotice = request.DemandNotice,
            OpenDispute = request.OpenDispute,
            ActivePromiseToPay = request.ActivePromiseToPay,
            TsiRemarks = request.TsiRemarks,
            SearchText = request.SearchText,
            Evidence = request.Evidence,
            Explanation = result.Explanation
        };

        await SaveAndStoreAsync(ArcWorkflowNodes.A1, message, context, cancellationToken);
        return message;
    }

    public async ValueTask<WorkflowMessage> A2Async(WorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        using var trace = TraceNode(ArcWorkflowNodes.A2, message.State);
        if (await TryResumeAsync(ArcWorkflowNodes.A2, message, context, cancellationToken) is { } resumed)
            return resumed;

        var exposure = RequireExposure(message);
        var cheques = await _cheques.ListChequesAsync(message.State.DealerUrn, cancellationToken);
        var bounced = cheques.Any(c => c.Status == ChequeStatus.Bounced);
        int? daysSinceNotice = message.DemandNotice is { } notice
            ? message.State.AsOf.DayNumber - notice.IssuedOn.DayNumber
            : null;

        var result = await _a2.RunAsync(
            new RiskPrioritisationAgentRequest(
                exposure,
                bounced,
                daysSinceNotice,
                message.TsiRemarks,
                ToAgentContext(message.State)),
            cancellationToken);

        message = message with
        {
            State = message.State.WithRisk(result.Assessment),
            Explanation = result.Explanation
        };
        await SaveAndStoreAsync(ArcWorkflowNodes.A2, message, context, cancellationToken);
        return message;
    }

    public async ValueTask<WorkflowMessage> A3Async(WorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        using var trace = TraceNode(ArcWorkflowNodes.A3, message.State);
        if (await TryResumeAsync(ArcWorkflowNodes.A3, message, context, cancellationToken) is { } resumed)
            return resumed;

        var dealer = RequireDealer(message);
        var exposure = RequireExposure(message);
        var result = await _a3.RunAsync(
            new NoticeDecisioningAgentRequest(
                dealer,
                exposure,
                message.OpenDispute,
                message.ActivePromiseToPay,
                message.SearchText,
                ToAgentContext(message.State)),
            cancellationToken);

        var state = message.State.WithNotice(result.Verdict);
        if (result.Verdict.Decision == NoticeDecision.Issue)
            state = state.WaitingFor(GateId.DepotManager);
        else
            state = state.WithStatus(WorkflowStatus.Terminated, $"A3 {result.Verdict.Decision} — Finance, no gate.");

        message = message with { State = state, Explanation = result.Explanation };
        await SaveAndStoreAsync(ArcWorkflowNodes.A3, message, context, cancellationToken);
        if (state.WaitingGate == GateId.DepotManager)
            await NotifyGateAsync(ArcWorkflowNodes.GateDepotManager, message, cancellationToken);
        return message;
    }

    public async ValueTask<WorkflowMessage> A4Async(WorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        using var trace = TraceNode(ArcWorkflowNodes.A4, message.State);
        if (await TryResumeAsync(ArcWorkflowNodes.A4, message, context, cancellationToken) is { } resumed)
            return resumed;

        if (message.Kind == ArcWorkflowKind.Section138)
        {
            var validation = await _documentExtraction.ValidateForSection138Async(
                message.State.DealerUrn,
                message.Evidence.Select(e => new ExtractionDocumentRef(e.Type, e.Location)).ToList(),
                message.State.CorrelationId.Value,
                cancellationToken);

            if (!validation.CanProgress)
            {
                _logger.LogInformation(
                    "Section 138 document validation blocked dealer {DealerUrn} correlation {CorrelationId}",
                    message.State.DealerUrn.Value,
                    message.State.CorrelationId.Value);
                var blocked = message with
                {
                    State = message.State.WithStatus(
                        WorkflowStatus.Blocked,
                        validation.Reason)
                };
                await SaveAndStoreAsync(ArcWorkflowNodes.A4, blocked, context, cancellationToken);
                return blocked;
            }
        }

        var exposure = RequireExposure(message);
        var result = await _a4.RunAsync(
            new LegalEligibilityAgentRequest(
                message.State.DealerUrn.Value,
                exposure,
                message.DemandNotice,
                ToAgentContext(message.State)),
            cancellationToken);

        var state = message.State.WithEligibility(
            result.Facts.Eligibility,
            result.Facts.Clock,
            result.Facts.SelectedCheque?.ChequeNumber,
            result.Facts.SelectedCheque?.Status.ToString(),
            result.Facts.Memo?.ReturnReasonCode,
            result.Facts.Memo?.MemoIssueDate,
            result.Facts.Memo?.MemoReceivedDate);
        if (!result.Facts.Eligibility.Eligible || result.Facts.Clock?.BlocksProgression == true)
            state = state.WithStatus(WorkflowStatus.Blocked, result.Facts.Eligibility.BlockReason ?? "S138 ineligible or clock expired.");
        else
            state = state.WaitingFor(GateId.LegalProgression);

        message = message with
        {
            State = state,
            Cheque = result.Facts.SelectedCheque,
            Memo = result.Facts.Memo,
            Explanation = result.Explanation
        };
        await SaveAndStoreAsync(ArcWorkflowNodes.A4, message, context, cancellationToken);
        if (state.WaitingGate == GateId.LegalProgression)
            await NotifyGateAsync(ArcWorkflowNodes.GateLegalProgression, message, cancellationToken);
        return message;
    }

    public async ValueTask<WorkflowMessage> A5Async(WorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        using var trace = TraceNode(ArcWorkflowNodes.A5, message.State);
        if (await TryResumeAsync(ArcWorkflowNodes.A5, message, context, cancellationToken) is { } resumed)
            return resumed;

        message = await HydrateAsync(message, cancellationToken);
        var dealer = RequireDealer(message);
        var exposure = RequireExposure(message);
        var kind = message.Kind == ArcWorkflowKind.Section138 ? DraftKind.Section138Notice : DraftKind.DemandNotice;
        var result = await _a5.RunAsync(
            new DraftingVerificationAgentRequest(
                kind,
                exposure,
                dealer,
                message.Cheque,
                message.Memo,
                message.State.Clock,
                Draft: null,
                ToAgentContext(message.State)),
            cancellationToken);

        var state = result.Verification.Passed
            ? message.State.WaitingFor(GateId.AdvocateSignature)
            : message.State.WithStatus(WorkflowStatus.Blocked, "A5 draft verification failed — no best-effort notice.");

        message = message with { State = state, Explanation = result.Explanation };
        await SaveAndStoreAsync(ArcWorkflowNodes.A5, message, context, cancellationToken);
        if (state.WaitingGate == GateId.AdvocateSignature)
            await NotifyGateAsync(ArcWorkflowNodes.GateAdvocateSignature, message, cancellationToken);
        return message;
    }

    public async ValueTask<WorkflowMessage> A6Async(WorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        using var trace = TraceNode(ArcWorkflowNodes.A6, message.State);
        if (await TryResumeAsync(ArcWorkflowNodes.A6, message, context, cancellationToken) is { } resumed)
            return resumed;

        var tier = message.State.Risk?.Tier ?? RecoveryTier.Visit;
        var result = await _a6.RunAsync(
            new FieldOrchestrationAgentRequest(
                FieldAgentAction.PlanVisit,
                message.State.DealerUrn.Value,
                tier,
                CommitmentDate: null,
                Amount: null,
                SpeechConfidence: null,
                ExistingPromise: null,
                VoiceTranscript: null,
                ToAgentContext(message.State)),
            cancellationToken);

        var state = message.State.WithStatus(WorkflowStatus.Completed);
        message = message with { State = state, Visit = result.Visit, Explanation = result.Explanation };
        if (result.Visit is { } visit)
            await _outbound.OnVisitPlannedAsync(visit, state, cancellationToken);
        if (message.Kind == ArcWorkflowKind.Odos && state.NoticeVerdict?.Decision == NoticeDecision.Issue)
            await _outbound.OnNoticeReadyAsync(state, cancellationToken);

        await SaveAndStoreAsync(ArcWorkflowNodes.A6, message, context, cancellationToken);
        return message;
    }

    public async ValueTask<WorkflowMessage> A7Async(WorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        using var trace = TraceNode(ArcWorkflowNodes.A7, message.State);
        if (await TryResumeAsync(ArcWorkflowNodes.A7, message, context, cancellationToken) is { } resumed)
            return resumed;

        var result = await _a7.RunAsync(
            new EvidenceCaseFileAgentRequest(
                message.State.DealerUrn.Value,
                message.Evidence,
                CaseReference: $"{message.State.CycleId.Value}|{message.State.DealerUrn.Value}",
                ToAgentContext(message.State)),
            cancellationToken);

        var state = message.State.WaitingFor(GateId.LegalCaseFileReview);
        message = message with { State = state, Explanation = result.Explanation };
        await SaveAndStoreAsync(ArcWorkflowNodes.A7, message, context, cancellationToken);
        await NotifyGateAsync(ArcWorkflowNodes.GateLegalCaseFileReview, message, cancellationToken);
        return message;
    }

    public ValueTask<WorkflowMessage> ApplyG1Async(GateApprovalResponse response, IWorkflowContext context, CancellationToken cancellationToken)
        => ApplyGateAsync(GateId.DepotManager, ActorRole.DepotManager, ArcWorkflowNodes.ApplyG1, response, context, cancellationToken);

    public ValueTask<WorkflowMessage> ApplyG2Async(GateApprovalResponse response, IWorkflowContext context, CancellationToken cancellationToken)
        => ApplyGateAsync(GateId.AdvocateSignature, ActorRole.Advocate, ArcWorkflowNodes.ApplyG2, response, context, cancellationToken);

    public ValueTask<WorkflowMessage> ApplyG3Async(GateApprovalResponse response, IWorkflowContext context, CancellationToken cancellationToken)
        => ApplyGateAsync(GateId.LegalProgression, ActorRole.Legal, ArcWorkflowNodes.ApplyG3, response, context, cancellationToken);

    public ValueTask<WorkflowMessage> ApplyG4Async(GateApprovalResponse response, IWorkflowContext context, CancellationToken cancellationToken)
        => ApplyGateAsync(GateId.LegalCaseFileReview, ActorRole.Legal, ArcWorkflowNodes.ApplyG4, response, context, cancellationToken);

    public async ValueTask<WorkflowMessage> TerminateAsync(WorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        if (message.State.Status is WorkflowStatus.Running or WorkflowStatus.WaitingForHuman)
            message = message with { State = message.State.WithStatus(WorkflowStatus.Terminated, message.State.TerminationReason ?? "Terminated.") };

        await SaveAndStoreAsync(ArcWorkflowNodes.Terminate, message, context, cancellationToken);
        return message;
    }

    public async ValueTask<WorkflowMessage> CompleteAsync(WorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        message = message with { State = message.State.WithStatus(WorkflowStatus.Completed) };
        await SaveAndStoreAsync(ArcWorkflowNodes.Complete, message, context, cancellationToken);
        return message;
    }

    private async ValueTask<WorkflowMessage> ApplyGateAsync(
        GateId gate,
        ActorRole expectedRole,
        string node,
        GateApprovalResponse response,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var stored = await RestoreMessageAsync(gate, response, context, cancellationToken);
        stored = await HydrateAsync(stored, cancellationToken);
        using var gateTrace = ArcTelemetry.StartGate(gate.ToString());
        ArcTelemetry.Set(gateTrace, "correlation_id", stored.State.CorrelationId.Value);
        ArcTelemetry.Set(gateTrace, "cycle_id", stored.State.CycleId.Value);
        ArcTelemetry.Set(gateTrace, "dealer_urn", stored.State.DealerUrn.Value);
        ArcTelemetry.Set(gateTrace, "actor", response.ActorUpn);
        ArcTelemetry.Set(gateTrace, "role", response.ActorRole.ToString());
        ArcTelemetry.Set(gateTrace, "verdict", response.Decision.ToString());

        GateDecision decision;
        if (response.Decision == GateDecisionStatus.Expired)
        {
            decision = GateDecision.Expire(gate, stored.State.CorrelationId);
        }
        else
        {
            if (response.ActorRole != expectedRole)
            {
                var blocked = stored with
                {
                    State = stored.State.WithStatus(WorkflowStatus.Blocked, $"Gate {gate} rejected: role {response.ActorRole} is not {expectedRole}.")
                };
                await SaveAndStoreAsync(node, blocked, context, cancellationToken);
                return blocked;
            }

            decision = GateDecision.Create(
                gate,
                response.ActorUpn,
                response.ActorRole,
                response.Decision,
                response.Reason,
                stored.State.CorrelationId,
                recommendedAction: stored.State.NoticeVerdict?.Decision.ToString());
        }

        await _gates.SaveAsync(stored.State.CycleId, stored.State.DealerUrn, decision, cancellationToken);
        var next = stored with { State = stored.State.WithApproval(decision) };
        if (!decision.AllowsProgression)
            next = next with { State = next.State.WithStatus(WorkflowStatus.Terminated, $"Gate {gate} {decision.Decision} — expiry is not approval.") };

        await SaveAndStoreAsync(node, next, context, cancellationToken);
        return next;
    }

    private async Task SaveAndStoreAsync(string node, WorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        await context.QueueStateUpdateAsync(ArcWorkflowNodes.StateKey, message, ArcWorkflowNodes.StateScope, cancellationToken);
        await _persistence.SaveAsync(node, message.State, cancellationToken);
        _logger.LogInformation(
            "Workflow node {Node} dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} status {Status}",
            node, message.State.DealerUrn.Value, message.State.CycleId.Value, message.State.CorrelationId.Value, message.State.Status);
    }

    private async Task NotifyGateAsync(string gateId, WorkflowMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var body = $"{gateId}|{message.State.CycleId.Value}|{message.State.DealerUrn.Value}|{message.State.CorrelationId.Value}";
            await _bus.PublishGateNotificationAsync(body, $"{gateId}|{message.State.CycleId.Value}|{message.State.DealerUrn.Value}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gate notification publish failed for {Gate} dealer {DealerUrn}. Workflow still suspended.", gateId, message.State.DealerUrn.Value);
        }
    }

    private async Task<WorkflowMessage?> TryResumeAsync(
        string node,
        WorkflowMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var loaded = await _persistence.LoadAsync(message.State, node, cancellationToken);
        if (loaded is null)
            return null;

        var resumed = message with { State = loaded.Value.State };
        await context.QueueStateUpdateAsync(ArcWorkflowNodes.StateKey, resumed, ArcWorkflowNodes.StateScope, cancellationToken);
        _logger.LogInformation(
            "Idempotent skip {Node} dealer {DealerUrn} cycle {CycleId}",
            node, message.State.DealerUrn.Value, message.State.CycleId.Value);
        return resumed;
    }

    private async Task<WorkflowMessage?> TryResumeAsync(
        string node,
        RecoveryState state,
        ArcWorkflowKind kind,
        Dealer dealer,
        WorkflowRunRequest request,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var loaded = await _persistence.LoadAsync(state, node, cancellationToken);
        if (loaded is null)
            return null;

        var resumed = new WorkflowMessage
        {
            State = loaded.Value.State,
            Kind = kind,
            Dealer = dealer,
            DemandNotice = request.DemandNotice,
            OpenDispute = request.OpenDispute,
            ActivePromiseToPay = request.ActivePromiseToPay,
            TsiRemarks = request.TsiRemarks,
            SearchText = request.SearchText,
            Evidence = request.Evidence
        };
        await context.QueueStateUpdateAsync(ArcWorkflowNodes.StateKey, resumed, ArcWorkflowNodes.StateScope, cancellationToken);
        _logger.LogInformation(
            "Idempotent skip {Node} dealer {DealerUrn} cycle {CycleId}",
            node, state.DealerUrn.Value, state.CycleId.Value);
        return resumed;
    }

    private async Task<WorkflowMessage> RestoreMessageAsync(
        GateId gate,
        GateApprovalResponse response,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var stored = await context.ReadStateAsync<WorkflowMessage>(ArcWorkflowNodes.StateKey, ArcWorkflowNodes.StateScope, cancellationToken);
        var urnValue = stored?.State.DealerUrn.Value;
        if (stored is not null && !string.IsNullOrWhiteSpace(urnValue))
            return stored;

        if (string.IsNullOrWhiteSpace(response.CycleId) || string.IsNullOrWhiteSpace(response.DealerUrn))
            throw new InvalidOperationException($"Missing workflow state at gate {gate} after checkpoint resume.");

        var latest = await _persistence.LoadLatestAsync(new CycleId(response.CycleId), new DealerUrn(response.DealerUrn), cancellationToken)
            ?? throw new InvalidOperationException($"RecoveryState missing at gate {gate} for {response.DealerUrn}.");

        var kind = stored?.Kind ?? KindFromGate(gate);
        return stored is null
            ? new WorkflowMessage { State = latest, Kind = kind }
            : stored with { State = latest };
    }

    private static ArcWorkflowKind KindFromGate(GateId gate) => gate switch
    {
        GateId.LegalProgression or GateId.LegalCaseFileReview => ArcWorkflowKind.Section138,
        _ => ArcWorkflowKind.Odos
    };

    private async Task<WorkflowMessage> HydrateAsync(WorkflowMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.State.DealerUrn.Value))
            return message;

        var dealer = HasDealer(message.Dealer)
            ? message.Dealer
            : await _dealers.GetAsync(message.State.DealerUrn, cancellationToken);
        var cheque = HasCheque(message.Cheque) ? message.Cheque : null;
        var memo = HasMemo(message.Memo) ? message.Memo : null;
        if (cheque is null || memo is null)
        {
            var cheques = await _cheques.ListChequesAsync(message.State.DealerUrn, cancellationToken);
            var memos = await _cheques.ListReturnMemosAsync(message.State.DealerUrn, cancellationToken);
            cheque ??= ChequeSelection.Select(cheques, memos);
            memo ??= cheque is null
                ? null
                : memos.FirstOrDefault(m => string.Equals(m.ChequeNumber, cheque.ChequeNumber, StringComparison.OrdinalIgnoreCase));
        }

        return message with { Dealer = dealer, Cheque = cheque, Memo = memo };
    }

    private static bool HasDealer(Dealer? dealer)
        => dealer is not null && !string.IsNullOrWhiteSpace(dealer.Urn.Value);

    private static bool HasCheque(SecurityCheque? cheque)
        => cheque is not null && !string.IsNullOrWhiteSpace(cheque.ChequeNumber);

    private static bool HasMemo(ChequeReturnMemo? memo)
        => memo is not null && !string.IsNullOrWhiteSpace(memo.ChequeNumber);

    private static AgentContext ToAgentContext(RecoveryState state)
        => new(state.AsOf, state.CycleId.Value, state.CorrelationId.Value, state.DealerUrn.Value);

    private static ExposureBreakdown RequireExposure(WorkflowMessage message)
        => message.State.Exposure ?? throw new InvalidOperationException("A1 exposure is required before this node.");

    private static Dealer RequireDealer(WorkflowMessage message)
        => message.Dealer ?? throw new InvalidOperationException("Dealer is required before this node.");

    private static IDisposable TraceNode(string node, RecoveryState state)
    {
        var cycle = ArcTelemetry.StartCycle(state.CycleId.Value);
        var dealer = ArcTelemetry.StartDealer(state.DealerUrn.Value);
        var executor = ArcTelemetry.StartExecutor(node);
        ArcTelemetry.Set(cycle, "correlation_id", state.CorrelationId.Value);
        ArcTelemetry.Set(dealer, "correlation_id", state.CorrelationId.Value);
        ArcTelemetry.Set(executor, "correlation_id", state.CorrelationId.Value);
        ArcTelemetry.Set(executor, "cycle_id", state.CycleId.Value);
        ArcTelemetry.Set(executor, "dealer_urn", state.DealerUrn.Value);
        return new TraceScope(cycle, dealer, executor);
    }

    private sealed class TraceScope : IDisposable
    {
        private readonly IDisposable? _cycle;
        private readonly IDisposable? _dealer;
        private readonly IDisposable? _executor;

        public TraceScope(IDisposable? cycle, IDisposable? dealer, IDisposable? executor)
        {
            _cycle = cycle;
            _dealer = dealer;
            _executor = executor;
        }

        public void Dispose()
        {
            _executor?.Dispose();
            _dealer?.Dispose();
            _cycle?.Dispose();
        }
    }
}
