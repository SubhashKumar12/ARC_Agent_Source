using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ARC.Agents.Workflows;
using ARC.Agents.Workflows.Models;
using ARC.Data.Cosmos;
using ARC.Data.Serialization;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Cli.Runtime;

internal sealed class CliRunResult
{
    public RecoveryState? State { get; init; }
    public WorkflowMessage? LastMessage { get; init; }
    public string? WaitingPort { get; init; }
    public bool HaltedForHuman { get; init; }
}

internal sealed class CliWorkflowDriver
{
    private readonly IServiceProvider _services;
    private readonly CheckpointManager _checkpoints;
    private readonly IWorkflowStateRepository _states;
    private readonly IConversationStateRepository _pending;
    private readonly IAuditRepository _audit;
    private readonly ILogger<CliWorkflowDriver> _logger;

    public CliWorkflowDriver(
        IServiceProvider services,
        CheckpointManager checkpoints,
        IWorkflowStateRepository states,
        IConversationStateRepository pending,
        IAuditRepository audit,
        ILogger<CliWorkflowDriver> logger)
    {
        _services = services;
        _checkpoints = checkpoints;
        _states = states;
        _pending = pending;
        _audit = audit;
        _logger = logger;
    }

    public static string SessionId(string cycleId, string dealerUrn, ArcWorkflowKind kind)
        => $"{cycleId}|{dealerUrn}|{kind}";

    public Task<CliRunResult> RunAsync(
        WorkflowRunRequest request,
        bool autoApproveGates,
        CancellationToken cancellationToken)
        => ExecuteAsync(request.CycleId, request.DealerUrn, request.Kind, autoApproveGates, start: request, resume: null, cancellationToken);

    public Task<CliRunResult> ResumeAsync(
        GateResumeRequest request,
        bool autoApproveRemaining,
        CancellationToken cancellationToken)
        => ExecuteAsync(request.CycleId, request.DealerUrn, request.Kind, autoApproveRemaining, start: null, resume: request, cancellationToken);

    private async Task<CliRunResult> ExecuteAsync(
        string cycleId,
        string dealerUrn,
        ArcWorkflowKind kind,
        bool autoApprove,
        WorkflowRunRequest? start,
        GateResumeRequest? resume,
        CancellationToken cancellationToken)
    {
        var sessionId = SessionId(cycleId, dealerUrn, kind);
        var workflow = Resolve(kind);
        await using StreamingRun run = resume is not null
            ? await ResumeFromPendingAsync(workflow, resume, cancellationToken)
            : await InProcessExecution.RunStreamingAsync(workflow, start!, _checkpoints, sessionId, cancellationToken);

        var firstResume = resume;
        while (true)
        {
            var drain = await DrainAsync(run, cycleId, dealerUrn, kind, sessionId, cancellationToken);
            if (!drain.HaltedForHuman)
                return drain;

            if (firstResume is { } named)
            {
                await SendNamedDecisionAsync(run, named, drain.WaitingPort!, cancellationToken);
                firstResume = null;
                continue;
            }

            if (!autoApprove)
                return drain;

            await SendApprovalAsync(run, drain.WaitingPort!, cycleId, dealerUrn, kind, cancellationToken);
        }
    }

    private async Task<StreamingRun> ResumeFromPendingAsync(
        Workflow workflow,
        GateResumeRequest request,
        CancellationToken cancellationToken)
    {
        var sessionId = SessionId(request.CycleId, request.DealerUrn, request.Kind);
        var cycle = new CycleId(request.CycleId);
        var urn = new DealerUrn(request.DealerUrn);
        var pendingJson = await _pending.GetAsync(cycle, urn, cancellationToken)
            ?? throw new InvalidOperationException($"No pending gate for {sessionId}.");
        var pending = ArcJson.Deserialize<PendingGateHalt>(pendingJson);
        var checkpoint = new CheckpointInfo(pending.SessionId, pending.CheckpointId);
        return await InProcessExecution.ResumeStreamingAsync(workflow, checkpoint, _checkpoints, cancellationToken);
    }

    private Workflow Resolve(ArcWorkflowKind kind)
    {
        var name = kind == ArcWorkflowKind.Section138 ? Section138Workflow.Name : OdosCycleWorkflow.Name;
        return _services.GetRequiredKeyedService<Workflow>(name);
    }

    private async Task<CliRunResult> DrainAsync(
        StreamingRun run,
        string cycleId,
        string dealerUrn,
        ArcWorkflowKind kind,
        string sessionId,
        CancellationToken cancellationToken)
    {
        CheckpointInfo? lastCheckpoint = null;
        RequestInfoEvent? pendingRequest = null;
        WorkflowMessage? lastMessage = null;

        await foreach (var workflowEvent in run.WatchStreamAsync(blockOnPendingRequest: false, cancellationToken))
        {
            switch (workflowEvent)
            {
                case SuperStepCompletedEvent step when step.CompletionInfo?.Checkpoint is { } checkpoint:
                    lastCheckpoint = checkpoint;
                    break;
                case RequestInfoEvent request:
                    pendingRequest = request;
                    break;
                case WorkflowErrorEvent error:
                    throw new InvalidOperationException("Workflow failed.", error.Exception);
                case WorkflowOutputEvent output when output.Is<WorkflowMessage>(out var message):
                    lastMessage = message;
                    _logger.LogInformation(
                        "CLI workflow output session {SessionId} status {Status} executor {ExecutorId}",
                        sessionId, message.State.Status, output.ExecutorId);
                    break;
            }
        }

        var cycle = new CycleId(cycleId);
        var urn = new DealerUrn(dealerUrn);
        var state = lastMessage?.State ?? await _states.LoadLatestStateAsync(cycle, urn, cancellationToken);

        if (pendingRequest is not null)
        {
            lastCheckpoint ??= await _checkpoints.GetLatestCheckpointAsync(sessionId, cancellationToken);
            if (lastCheckpoint is null)
                throw new InvalidOperationException($"Gate {pendingRequest.Request.PortInfo.PortId} suspended without a checkpoint.");

            var halt = new PendingGateHalt(
                lastCheckpoint.SessionId,
                lastCheckpoint.CheckpointId,
                pendingRequest.Request.RequestId,
                pendingRequest.Request.PortInfo.PortId,
                kind);
            await _pending.SaveAsync(cycle, urn, ArcJson.Serialize(halt), cancellationToken);
            await _audit.AppendAsync(
                new AuditEvent("gate_suspend", cycleId, dealerUrn, pendingRequest.Request.RequestId, DateTimeOffset.UtcNow, halt.PortId),
                cancellationToken);
            _logger.LogInformation(
                "CLI workflow suspended session {SessionId} gate {Gate} checkpoint {CheckpointId}",
                sessionId, halt.PortId, halt.CheckpointId);

            return new CliRunResult
            {
                State = state,
                LastMessage = lastMessage,
                WaitingPort = halt.PortId,
                HaltedForHuman = true
            };
        }

        await _pending.SaveAsync(cycle, urn, ArcJson.Serialize(new { status = "cleared", sessionId }), cancellationToken);
        return new CliRunResult
        {
            State = state,
            LastMessage = lastMessage,
            HaltedForHuman = false
        };
    }

    private async Task SendNamedDecisionAsync(
        StreamingRun run,
        GateResumeRequest request,
        string portId,
        CancellationToken cancellationToken)
    {
        var cycle = new CycleId(request.CycleId);
        var urn = new DealerUrn(request.DealerUrn);
        var pendingJson = await _pending.GetAsync(cycle, urn, cancellationToken)
            ?? throw new InvalidOperationException($"No pending gate for resume {portId}.");
        var pending = ArcJson.Deserialize<PendingGateHalt>(pendingJson);
        var state = await _states.LoadLatestStateAsync(cycle, urn, cancellationToken)
            ?? throw new InvalidOperationException("RecoveryState missing for resume.");

        var port = RequestPort.Create<WorkflowMessage, GateApprovalResponse>(portId);
        var envelope = ExternalRequest.Create(port, new WorkflowMessage { State = state, Kind = request.Kind }, pending.RequestId);
        var decision = new GateApprovalResponse(
            request.ActorUpn,
            request.ActorRole,
            request.Decision,
            request.Reason,
            request.CycleId,
            request.DealerUrn);
        await run.SendResponseAsync(envelope.CreateResponse(decision));
        await _audit.AppendAsync(
            new AuditEvent("gate_resume", request.CycleId, request.DealerUrn, state.CorrelationId.Value, DateTimeOffset.UtcNow, portId),
            cancellationToken);
    }

    private async Task SendApprovalAsync(
        StreamingRun run,
        string portId,
        string cycleId,
        string dealerUrn,
        ArcWorkflowKind kind,
        CancellationToken cancellationToken)
    {
        var cycle = new CycleId(cycleId);
        var urn = new DealerUrn(dealerUrn);
        var pendingJson = await _pending.GetAsync(cycle, urn, cancellationToken)
            ?? throw new InvalidOperationException($"No pending gate for auto-approve {portId}.");
        var pending = ArcJson.Deserialize<PendingGateHalt>(pendingJson);
        var (role, upn) = RoleForPort(portId);
        var state = await _states.LoadLatestStateAsync(cycle, urn, cancellationToken)
            ?? throw new InvalidOperationException("RecoveryState missing for auto-approve.");

        var port = RequestPort.Create<WorkflowMessage, GateApprovalResponse>(portId);
        var envelope = ExternalRequest.Create(port, new WorkflowMessage { State = state, Kind = kind }, pending.RequestId);
        var decision = new GateApprovalResponse(
            upn,
            role,
            GateDecisionStatus.Approved,
            "CLI Shadow demo approval — not a production signature.",
            cycleId,
            dealerUrn);
        await run.SendResponseAsync(envelope.CreateResponse(decision));
        await _audit.AppendAsync(
            new AuditEvent("gate_cli_auto_approve", cycleId, dealerUrn, state.CorrelationId.Value, DateTimeOffset.UtcNow, portId),
            cancellationToken);
    }

    internal static (ActorRole Role, string Upn) RoleForPort(string portId) => portId switch
    {
        ArcWorkflowNodes.GateDepotManager => (ActorRole.DepotManager, "depot.manager@paintco.local"),
        ArcWorkflowNodes.GateAdvocateSignature => (ActorRole.Advocate, "advocate@paintco.local"),
        ArcWorkflowNodes.GateLegalProgression => (ActorRole.Legal, "legal@paintco.local"),
        ArcWorkflowNodes.GateLegalCaseFileReview => (ActorRole.Legal, "legal@paintco.local"),
        _ => throw new InvalidOperationException($"Unknown gate port '{portId}'.")
    };
}
