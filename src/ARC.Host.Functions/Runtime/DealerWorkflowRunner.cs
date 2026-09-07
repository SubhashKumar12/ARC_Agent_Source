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

namespace ARC.Host.Functions.Runtime;

public sealed class DealerWorkflowRunner
{
    private readonly IServiceProvider _services;
    private readonly CheckpointManager _checkpoints;
    private readonly IWorkflowStateRepository _states;
    private readonly IConversationStateRepository _pending;
    private readonly IAuditRepository _audit;
    private readonly ILogger<DealerWorkflowRunner> _logger;

    public DealerWorkflowRunner(
        IServiceProvider services,
        CheckpointManager checkpoints,
        IWorkflowStateRepository states,
        IConversationStateRepository pending,
        IAuditRepository audit,
        ILogger<DealerWorkflowRunner> logger)
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

    public async Task RunAsync(WorkflowRunRequest request, CancellationToken cancellationToken)
    {
        var sessionId = SessionId(request.CycleId, request.DealerUrn, request.Kind);
        var cycle = new CycleId(request.CycleId);
        var urn = new DealerUrn(request.DealerUrn);
        var latest = await _states.LoadLatestStateAsync(cycle, urn, cancellationToken);

        if (latest is { Status: WorkflowStatus.Completed or WorkflowStatus.Terminated or WorkflowStatus.Blocked })
        {
            _logger.LogInformation(
                "Skip start session {SessionId} status {Status}",
                sessionId, latest.Status);
            return;
        }

        if (latest?.Status == WorkflowStatus.WaitingForHuman)
        {
            _logger.LogInformation("Skip start session {SessionId}; waiting for human gate.", sessionId);
            return;
        }

        var workflow = Resolve(request.Kind);
        var mafLatest = await _checkpoints.GetLatestCheckpointAsync(sessionId, cancellationToken);
        await using StreamingRun run = mafLatest is not null
            ? await InProcessExecution.ResumeStreamingAsync(workflow, mafLatest, _checkpoints, cancellationToken)
            : await InProcessExecution.RunStreamingAsync(workflow, request, _checkpoints, sessionId, cancellationToken);

        await DrainAsync(run, request.CycleId, request.DealerUrn, request.Kind, sessionId, cancellationToken);
    }

    public async Task ResumeAsync(GateResumeRequest request, CancellationToken cancellationToken)
    {
        var sessionId = SessionId(request.CycleId, request.DealerUrn, request.Kind);
        var cycle = new CycleId(request.CycleId);
        var urn = new DealerUrn(request.DealerUrn);
        var pendingJson = await _pending.GetAsync(cycle, urn, cancellationToken)
            ?? throw new InvalidOperationException($"No pending gate for {sessionId}.");
        var pending = ArcJson.Deserialize<PendingGateHalt>(pendingJson);
        if (pending.Kind != request.Kind)
            throw new InvalidOperationException($"Pending gate kind {pending.Kind} does not match resume {request.Kind}.");

        var workflow = Resolve(request.Kind);
        var checkpoint = new CheckpointInfo(pending.SessionId, pending.CheckpointId);
        await using var run = await InProcessExecution.ResumeStreamingAsync(workflow, checkpoint, _checkpoints, cancellationToken);

        var port = RequestPort.Create<WorkflowMessage, GateApprovalResponse>(pending.PortId);
        var state = await _states.LoadLatestStateAsync(cycle, urn, cancellationToken)
            ?? throw new InvalidOperationException($"RecoveryState missing for {sessionId}.");
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
            new AuditEvent("gate_resume", request.CycleId, request.DealerUrn, state.CorrelationId.Value, DateTimeOffset.UtcNow, pending.PortId),
            cancellationToken);

        await DrainAsync(run, request.CycleId, request.DealerUrn, request.Kind, sessionId, cancellationToken);
    }

    private Workflow Resolve(ArcWorkflowKind kind)
    {
        var name = kind == ArcWorkflowKind.Section138 ? Section138Workflow.Name : OdosCycleWorkflow.Name;
        return _services.GetRequiredKeyedService<Workflow>(name);
    }

    private async Task DrainAsync(
        StreamingRun run,
        string cycleId,
        string dealerUrn,
        ArcWorkflowKind kind,
        string sessionId,
        CancellationToken cancellationToken)
    {
        CheckpointInfo? lastCheckpoint = null;
        RequestInfoEvent? pendingRequest = null;

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
                    _logger.LogInformation(
                        "Workflow output session {SessionId} status {Status} executor {ExecutorId}",
                        sessionId, message.State.Status, output.ExecutorId);
                    break;
            }
        }

        var cycle = new CycleId(cycleId);
        var urn = new DealerUrn(dealerUrn);

        if (pendingRequest is not null)
        {
            lastCheckpoint ??= await _checkpoints.GetLatestCheckpointAsync(sessionId, cancellationToken);
            if (lastCheckpoint is null)
                throw new InvalidOperationException($"Gate {pendingRequest.Request.PortInfo.PortId} suspended without a Cosmos checkpoint.");

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
                "Workflow suspended session {SessionId} gate {Gate} checkpoint {CheckpointId}",
                sessionId, halt.PortId, halt.CheckpointId);
            return;
        }

        await _pending.SaveAsync(cycle, urn, ArcJson.Serialize(new { status = "cleared", sessionId }), cancellationToken);
        _logger.LogInformation("Workflow finished session {SessionId}", sessionId);
    }
}
