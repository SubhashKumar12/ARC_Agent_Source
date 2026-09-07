using Microsoft.AspNetCore.Mvc;
using ARC.Agents.A8SupervisoryInsight;
using ARC.Agents.Context;
using ARC.Agents.Models;
using ARC.Api.Auth;
using ARC.Api.DTOs;
using ARC.Data.Sql;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Api.Controllers;

[ApiController]
[Route("v1/insights")]
public sealed class InsightsController : ControllerBase
{
    private readonly SupervisoryInsightAgent _a8;
    private readonly IDealerRepository _dealers;

    public InsightsController(SupervisoryInsightAgent a8, IDealerRepository dealers)
    {
        _a8 = a8;
        _dealers = dealers;
    }

    [HttpGet("exceptions")]
    public async Task<IActionResult> Exceptions(
        [FromQuery] string cycleId,
        [FromQuery] string? dealerUrn,
        [FromQuery] string? region,
        CancellationToken cancellationToken)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        var denied = await AuthorizeDealerAsync(actor, dealerUrn, cancellationToken);
        if (denied is not null)
            return denied;
        if (!TryBuildRequest(actor, cycleId, dealerUrn, region, question: null, out var request, out var error))
            return BadRequest(new { error });
        var result = await _a8.RunAsync(request, cancellationToken);
        return Ok(ToSupervisionResponse(result, includeWorklist: true, includeSources: false));
    }

    [HttpPost("nlq")]
    public async Task<IActionResult> Nlq([FromBody] NlqRequest body, CancellationToken cancellationToken)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        var denied = await AuthorizeDealerAsync(actor, body.DealerUrn, cancellationToken);
        if (denied is not null)
            return denied;
        if (!TryBuildRequest(actor, body.CycleId, body.DealerUrn, body.Region, body.Question, out var request, out var error))
            return BadRequest(new { error });
        var result = await _a8.RunAsync(request, cancellationToken);
        return Ok(ToSupervisionResponse(result, includeWorklist: false, includeSources: true));
    }

    private async Task<IActionResult?> AuthorizeDealerAsync(ArcActor actor, string? dealerUrn, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dealerUrn))
            return null;
        var dealer = await _dealers.GetAsync(new DealerUrn(dealerUrn), cancellationToken);
        if (dealer is null)
            return NotFound(new { error = "Dealer was not found." });
        if (!GateAccess.CanReadDealer(actor, dealer.Region, dealer.Depot))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Dealer is outside the actor's region or depot." });
        return null;
    }

    private static bool TryBuildRequest(
        ArcActor actor,
        string cycleId,
        string? dealerUrn,
        string? region,
        string? question,
        out SupervisoryInsightAgentRequest request,
        out string? error)
    {
        var scopedRegion = GateAccess.ForcedRegion(actor) ?? region;
        var scopedDepot = GateAccess.ForcedDepot(actor);
        if (string.IsNullOrWhiteSpace(dealerUrn) && string.IsNullOrWhiteSpace(scopedRegion))
        {
            request = null!;
            error = "Provide dealerUrn or region. TSI region is always taken from the authenticated actor.";
            return false;
        }

        request = new SupervisoryInsightAgentRequest(
            cycleId,
            scopedRegion,
            dealerUrn,
            question,
            PromisesToPay: null,
            new AgentContext(DateOnly.FromDateTime(DateTime.UtcNow), cycleId, CorrelationId.New().Value, dealerUrn),
            scopedDepot);
        error = null;
        return true;
    }

    private static object ToSupervisionResponse(
        SupervisoryInsightAgentResult result,
        bool includeWorklist,
        bool includeSources)
    {
        var chat = result.ChatSafe;
        var body = new Dictionary<string, object?>
        {
            ["cycleId"] = chat.CycleId,
            ["region"] = chat.Region,
            ["depot"] = chat.Depot,
            ["exceptions"] = chat.Exceptions.Select(MapException).ToList(),
            ["dealers"] = chat.Dealers.Select(MapDealer).ToList(),
            ["deterministicExplanation"] = chat.DeterministicExplanation,
            ["provenanceStatus"] = chat.Provenance.Status,
            ["dataCompleteness"] = chat.DataCompleteness.Select(c => new
            {
                fact = c.Fact,
                status = c.Status.ToString(),
                detail = c.Detail
            }).ToList(),
            ["tbcIndicators"] = chat.TbcIndicators.Select(t => new
            {
                id = t.Id,
                status = t.Status,
                detail = t.Detail
            }).ToList(),
            ["productionSupervisionBlockedOnApprovedStoredProcedure"] = chat.ProductionSupervisionBlockedOnApprovedStoredProcedure,
            ["productionStoredProcedureBlockers"] = chat.ProductionStoredProcedureBlockers,
            ["explanation"] = result.Explanation
        };

        if (includeWorklist)
        {
            body["worklist"] = result.Insights.Worklist.Select(e =>
            {
                var a2 = A2ChatSafeAssembler.FromWorklistEntry(
                    e.DealerUrn.Value,
                    e.RecoverabilityScore,
                    e.RecoveryTier,
                    e.Rank,
                    isTopDecile: null,
                    visitCutoffConfigured: false);
                return new
                {
                    e.Rank,
                    dealerUrn = e.DealerUrn.Value,
                    e.RecoverabilityScore,
                    e.RecoveryTier,
                    e.Status,
                    scoreFormula = a2.ScoreFormula,
                    provenanceStatus = a2.Provenance.Status,
                    dataCompleteness = a2.DataCompleteness,
                    tbcIndicators = a2.TbcIndicators
                };
            });
        }

        if (includeSources)
        {
            body["sources"] = result.Grounding?.KnowledgeChunks.Select(k => new
            {
                k.Reference.DocumentId,
                k.Title,
                k.Reference.SourceSystem,
                k.Snippet
            });
        }

        return body;
    }

    private static object MapException(A8ExceptionFact e) => new
    {
        cycleId = e.CycleId,
        dealerUrn = e.DealerUrn,
        exceptionKind = e.ExceptionKind,
        exceptionDetail = e.ExceptionDetail,
        workflowStatus = e.WorkflowStatus,
        waitingGate = e.WaitingGate,
        region = e.Region,
        depot = e.Depot,
        gates = e.Gates.Select(MapGate).ToList()
    };

    private static object MapDealer(A8DealerFact d) => new
    {
        dealerUrn = d.DealerUrn,
        workflowStatus = d.WorkflowStatus,
        waitingGate = d.WaitingGate,
        region = d.Region,
        depot = d.Depot,
        gates = d.Gates.Select(MapGate).ToList()
    };

    private static object MapGate(A8GateMetadata g) => new
    {
        gate = g.Gate,
        actorUpn = g.ActorUpn,
        actorRole = g.ActorRole,
        decision = g.Decision,
        reason = g.Reason,
        recommendedAction = g.RecommendedAction,
        decidedUtc = g.DecidedUtc,
        correlationId = g.CorrelationId,
        wasOverride = g.WasOverride
    };
}
