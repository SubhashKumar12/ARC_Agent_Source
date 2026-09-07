using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Microsoft.Agents.AI.Workflows;
using ARC.Agents.Workflows;
using ARC.Agents.Workflows.Models;
using ARC.Data.Cosmos;
using ARC.Data.Serialization;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Web.Services;

namespace ARC.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IServiceProvider _services;
    private readonly DemoDataSeeder _seeder;
    private readonly CheckpointManager _checkpoints;
    private readonly IWorkflowStateRepository _workflowStates;
    private readonly IConversationStateRepository _conversationStates;
    private readonly IGateDecisionRepository _gateDecisions;
    private readonly IAuditRepository _audit;
    private readonly WebDemoOutboundGate _outboundGate;
    private readonly IBusinessChatApiClient _businessChat;
    private readonly ArcWebBusinessChatOptions _chatOptions;

    public string ActorDisplayName { get; private set; } = "";
    public string ActorRoleDisplay { get; private set; } = "";
    public string ApiBaseUrl { get; private set; } = "";

    public IndexModel(
        ILogger<IndexModel> logger,
        IServiceProvider services,
        DemoDataSeeder seeder,
        CheckpointManager checkpoints,
        IWorkflowStateRepository workflowStates,
        IConversationStateRepository conversationStates,
        IGateDecisionRepository gateDecisions,
        IAuditRepository audit,
        WebDemoOutboundGate outboundGate,
        IBusinessChatApiClient businessChat,
        IOptions<ArcWebBusinessChatOptions> chatOptions)
    {
        _logger = logger;
        _services = services;
        _seeder = seeder;
        _checkpoints = checkpoints;
        _workflowStates = workflowStates;
        _conversationStates = conversationStates;
        _gateDecisions = gateDecisions;
        _audit = audit;
        _outboundGate = outboundGate;
        _businessChat = businessChat;
        _chatOptions = chatOptions.Value;
    }

    public void OnGet()
    {
        ActorDisplayName = _chatOptions.ActorUpn;
        ActorRoleDisplay = _chatOptions.ActorRole;
        ApiBaseUrl = _chatOptions.ApiBaseUrl;
        _logger.LogInformation("ARC.Web chat page loaded");
    }

    public async Task<IActionResult> OnGetApiHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var url = $"{_chatOptions.ApiBaseUrl.TrimEnd('/')}/health";
            var response = await client.GetAsync(url, cancellationToken);
            return new JsonResult(new
            {
                healthy = response.IsSuccessStatusCode,
                status = response.IsSuccessStatusCode ? "connected" : "unavailable"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ARC.Api health check failed for {ApiBaseUrl}", _chatOptions.ApiBaseUrl);
            return new JsonResult(new { healthy = false, status = "unreachable" });
        }
    }

    public async Task<IActionResult> OnPostBusinessChatAsync(
        [FromBody] BusinessChatProxyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Message))
                return BadRequest(new { error = "Message is required." });

            var response = await _businessChat.SendAsync(request, cancellationToken);
            return new JsonResult(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Business chat API unreachable at {ApiBaseUrl}", _chatOptions.ApiBaseUrl);
            Response.StatusCode = 503;
            return new JsonResult(new
            {
                error = "ARC service is temporarily unavailable. Please try again.",
                errorType = "api_unreachable"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Business chat proxy failed");
            Response.StatusCode = 503;
            return new JsonResult(new
            {
                error = "ARC service is temporarily unavailable. Please try again.",
                errorType = "api_unreachable"
            });
        }
    }

    public async Task<IActionResult> OnPostStartScenarioAsync([FromBody] StartScenarioRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var scenarioId = request.ScenarioId.ToUpperInvariant();
            var dealerUrn = $"dealer:{scenarioId.ToLowerInvariant()}";
            var cycleId = $"2026-09-{scenarioId.ToLowerInvariant()}";

            _logger.LogInformation("Starting scenario {ScenarioId}", scenarioId);

            // Seed demo data
            await _seeder.SeedScenarioAsync(scenarioId, cancellationToken);

            // Determine workflow type
            var kind = scenarioId == "S3" ? ArcWorkflowKind.Section138 : ArcWorkflowKind.Odos;
            var workflowName = kind == ArcWorkflowKind.Section138 ? Section138Workflow.Name : OdosCycleWorkflow.Name;
            var workflow = _services.GetRequiredKeyedService<Workflow>(workflowName);

            var runRequest = new WorkflowRunRequest
            {
                CycleId = cycleId,
                DealerUrn = dealerUrn,
                Kind = kind,
                AsOf = new DateOnly(2026, 3, 1),
                CorrelationId = Guid.NewGuid().ToString("N"),
                Mode = RunMode.Shadow
            };

            var sessionId = $"{cycleId}|{dealerUrn}|{kind}";

            // Start workflow execution in background (non-blocking)
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var run = await InProcessExecution.RunStreamingAsync(
                        workflow, 
                        runRequest, 
                        _checkpoints, 
                        sessionId, 
                        CancellationToken.None); // Use None for background task

                    await DrainWorkflowAsync(run, cycleId, dealerUrn, kind, sessionId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Workflow execution failed for {SessionId}", sessionId);
                }
            }, CancellationToken.None);

            // Return immediately so UI can start polling
            return new JsonResult(new { success = true, cycleId, dealerUrn, kind = kind.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start scenario {ScenarioId}", request.ScenarioId);
            Response.StatusCode = 500;
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public async Task<IActionResult> OnPostSubmitGateDecisionAsync([FromBody] WebGateDecisionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var cycleId = new CycleId(request.CycleId);
            var dealerUrn = new DealerUrn(request.DealerUrn);
            var actorRole = ActorRole.DepotManager; // Demo default role

            var decisionStatus = request.Decision == "Approved" ? GateDecisionStatus.Approved : GateDecisionStatus.Declined;
            
            // Parse gate ID from string (G1 -> DepotManager, G2 -> AdvocateSignature, etc.)
            var gateId = request.GateId switch
            {
                "G1" => GateId.DepotManager,
                "G2" => GateId.AdvocateSignature,
                "G3" => GateId.LegalProgression,
                "G4" => GateId.LegalCaseFileReview,
                _ => GateId.DepotManager
            };
            
            var decision = GateDecision.Create(
                gate: gateId,
                actorUpn: "demo.manager@arc.local",
                actorRole: actorRole,
                decision: decisionStatus,
                reason: request.Reason ?? "Demo decision",
                correlationId: new CorrelationId(Guid.NewGuid().ToString("N")));

            await _gateDecisions.SaveAsync(cycleId, dealerUrn, decision, cancellationToken);

            // Resume workflow in background
            var kind = request.Kind == "Section138" ? ArcWorkflowKind.Section138 : ArcWorkflowKind.Odos;
            var sessionId = $"{request.CycleId}|{request.DealerUrn}|{kind}";

            _ = Task.Run(async () =>
            {
                try
                {
                    var workflowName = kind == ArcWorkflowKind.Section138 ? Section138Workflow.Name : OdosCycleWorkflow.Name;
                    var workflow = _services.GetRequiredKeyedService<Workflow>(workflowName);
                    
                    // Load pending halt
                    var pendingJson = await _conversationStates.GetAsync(cycleId, dealerUrn, CancellationToken.None);
                    if (string.IsNullOrEmpty(pendingJson))
                    {
                        _logger.LogWarning("No pending gate halt found for {SessionId}", sessionId);
                        return;
                    }

                    var halt = ArcJson.Deserialize<PendingGateHalt>(pendingJson);

                    // Load current state
                    var state = await _workflowStates.LoadLatestStateAsync(cycleId, dealerUrn, CancellationToken.None);
                    if (state == null)
                    {
                        _logger.LogWarning("No state found for {SessionId}", sessionId);
                        return;
                    }

                    // Resume from checkpoint
                    var checkpointInfo = new CheckpointInfo(halt.SessionId, halt.CheckpointId);
                    await using var run = await InProcessExecution.ResumeStreamingAsync(
                        workflow,
                        checkpointInfo,
                        _checkpoints,
                        CancellationToken.None);

                    // Send decision response to resume port
                    var port = RequestPort.Create<WorkflowMessage, GateApprovalResponse>(halt.PortId);
                    var envelope = ExternalRequest.Create(port, new WorkflowMessage { State = state, Kind = kind }, halt.RequestId);
                    var response = new GateApprovalResponse(
                        "demo.manager@arc.local",
                        actorRole,
                        decisionStatus,
                        request.Reason ?? "Demo decision",
                        request.CycleId,
                        request.DealerUrn);
                    
                    await run.SendResponseAsync(envelope.CreateResponse(response));
                    await _audit.AppendAsync(
                        new AuditEvent("gate_resume", request.CycleId, request.DealerUrn, state.CorrelationId.Value, DateTimeOffset.UtcNow, halt.PortId),
                        CancellationToken.None);

                    await DrainWorkflowAsync(run, request.CycleId, request.DealerUrn, kind, sessionId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Workflow resume failed for {SessionId}", sessionId);
                }
            }, CancellationToken.None);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit gate decision");
            Response.StatusCode = 500;
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public async Task<IActionResult> OnGetWorkflowStatusAsync(string cycleId, string dealerUrn, CancellationToken cancellationToken)
    {
        try
        {
            var state = await _workflowStates.LoadLatestStateAsync(
                new CycleId(cycleId),
                new DealerUrn(dealerUrn),
                cancellationToken);

            if (state == null)
                return NotFound();

            var result = new
            {
                status = state.Status.ToString(),
                waitingGate = state.WaitingGate,
                exposureAmount = state.Exposure?.GrossOpenAr.Amount,
                riskTier = state.Risk?.Tier.ToString(),
                noticeDecision = state.NoticeVerdict?.Decision.ToString(),
                eligibility = state.Eligibility?.Eligible.ToString()
            };

            return new JsonResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow status");
            Response.StatusCode = 500;
            return new JsonResult(new { success = false, error = ex.Message });
        }
    }

    public IActionResult OnGetShadowActions()
    {
        var actions = _outboundGate.RecordedActions;
        return new JsonResult(new { actions = actions.Select(a => new { a.Type, a.Summary }) });
    }

    private async Task DrainWorkflowAsync(
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
                    _logger.LogError(error.Exception, "Workflow error in {SessionId}", sessionId);
                    throw new InvalidOperationException("Workflow failed.", error.Exception);
                case WorkflowOutputEvent output when output.Is<WorkflowMessage>(out var message):
                    lastMessage = message;
                    _logger.LogInformation(
                        "Workflow output session {SessionId} status {Status}",
                        sessionId, message.State.Status);
                    break;
            }
        }

        var cycle = new CycleId(cycleId);
        var urn = new DealerUrn(dealerUrn);
        var state = lastMessage?.State ?? await _workflowStates.LoadLatestStateAsync(cycle, urn, cancellationToken);

        if (pendingRequest is not null)
        {
            lastCheckpoint ??= await _checkpoints.GetLatestCheckpointAsync(sessionId, cancellationToken);
            if (lastCheckpoint is null)
            {
                _logger.LogError("Gate {PortId} suspended without a checkpoint", pendingRequest.Request.PortInfo.PortId);
                return;
            }

            var halt = new PendingGateHalt(
                lastCheckpoint.SessionId,
                lastCheckpoint.CheckpointId,
                pendingRequest.Request.RequestId,
                pendingRequest.Request.PortInfo.PortId,
                kind);
            
            await _conversationStates.SaveAsync(cycle, urn, ArcJson.Serialize(halt), cancellationToken);
            await _audit.AppendAsync(
                new AuditEvent("gate_suspend", cycleId, dealerUrn, pendingRequest.Request.RequestId, DateTimeOffset.UtcNow, halt.PortId),
                cancellationToken);
            
            _logger.LogInformation(
                "Workflow suspended session {SessionId} gate {Gate} checkpoint {CheckpointId}",
                sessionId, halt.PortId, halt.CheckpointId);
        }
        else
        {
            // Cleared - no pending gate
            await _conversationStates.SaveAsync(cycle, urn, ArcJson.Serialize(new { status = "cleared", sessionId }), cancellationToken);
            _logger.LogInformation("Workflow completed session {SessionId}", sessionId);
        }
    }
}

public sealed record StartScenarioRequest(string ScenarioId);
public sealed record WebGateDecisionRequest(string CycleId, string DealerUrn, string GateId, string Decision, string? Reason, string Kind);
