using Microsoft.AspNetCore.Mvc;
using ARC.Api.Auth;
using ARC.Api.DTOs;
using ARC.Data.Cosmos;
using ARC.Data.Sql;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Api.Controllers;

[ApiController]
[Route("v1/cycles/{cycleId}")]
public sealed class CasesController : ControllerBase
{
    private readonly IRecoveryCaseRepository _cases;
    private readonly IDealerRepository _dealers;
    private readonly IWorkflowStateRepository _states;
    private readonly IGateDecisionRepository _gates;

    public CasesController(
        IRecoveryCaseRepository cases,
        IDealerRepository dealers,
        IWorkflowStateRepository states,
        IGateDecisionRepository gates)
    {
        _cases = cases;
        _dealers = dealers;
        _states = states;
        _gates = gates;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<CycleDashboardDto>> Dashboard(string cycleId, CancellationToken cancellationToken)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        var rows = await _cases.ListByCycleAsync(
            new CycleId(cycleId),
            GateAccess.ForcedRegion(actor),
            GateAccess.ForcedDepot(actor),
            cancellationToken);

        var byStatus = rows.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        var waiting = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.WaitingGate))
            .GroupBy(r => r.WaitingGate!)
            .ToDictionary(g => g.Key, g => g.Count());
        return Ok(new CycleDashboardDto(cycleId, rows.Count, byStatus, waiting));
    }

    [HttpGet("cases")]
    public async Task<ActionResult<IReadOnlyList<CaseSummaryDto>>> List(
        string cycleId,
        [FromQuery] bool waitingOnly = false,
        CancellationToken cancellationToken = default)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        var rows = await _cases.ListByCycleAsync(
            new CycleId(cycleId),
            GateAccess.ForcedRegion(actor),
            GateAccess.ForcedDepot(actor),
            cancellationToken);
        if (waitingOnly)
            rows = rows.Where(r => !string.IsNullOrWhiteSpace(r.WaitingGate)).ToList();

        return Ok(rows.Select(r => new CaseSummaryDto(
            r.CycleId.Value, r.DealerUrn.Value, r.Status, r.WaitingGate, r.CorrelationId, r.UpdatedUtc)).ToList());
    }

    [HttpGet("worklist")]
    public async Task<ActionResult<RankedWorklistDto>> Worklist(
        string cycleId,
        [FromQuery] bool topDecile = false,
        CancellationToken cancellationToken = default)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        var worklist = await _cases.ListRankedWorklistAsync(
            new CycleId(cycleId),
            GateAccess.ForcedRegion(actor),
            GateAccess.ForcedDepot(actor),
            topDecile,
            cancellationToken);

        var tbc = A2ChatSafeAssembler.CentralTbcCatalog(visitCutoffConfigured: false);
        var items = worklist.Entries.Select(e =>
        {
            var chat = A2ChatSafeAssembler.FromWorklistEntry(
                e.DealerUrn.Value,
                e.RecoverabilityScore,
                e.RecoveryTier,
                e.Rank,
                isTopDecile: e.Rank <= RecoverabilityWorklist.TopDecileSize(worklist.EligibleCount),
                visitCutoffConfigured: false);
            return new WorklistEntryDto(
                e.Rank,
                e.DealerUrn.Value,
                e.RecoverabilityScore,
                e.RecoveryTier,
                e.Status,
                e.WaitingGate,
                chat.ScoreFormula,
                chat.IsTopDecile,
                chat.DeterministicExplanation,
                chat.Provenance.Status,
                chat.DataCompleteness,
                chat.TbcIndicators);
        }).ToList();

        return Ok(new RankedWorklistDto(
            cycleId,
            worklist.TopDecile,
            worklist.EligibleCount,
            items.Count,
            items,
            RecoverabilityScoreFormula.Id,
            ProductionChatWorklistBlockedOnApprovedStoredProcedure: true,
            tbc));
    }

    [HttpGet("dealers/{dealerUrn}")]
    public async Task<ActionResult<CaseDetailDto>> Get(string cycleId, string dealerUrn, CancellationToken cancellationToken)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        var urn = new DealerUrn(dealerUrn);
        var dealer = await _dealers.GetAsync(urn, cancellationToken);
        if (dealer is null)
            return NotFound();
        if (!GateAccess.CanReadDealer(actor, dealer.Region, dealer.Depot))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Dealer is outside the actor's region or depot." });

        var cycle = new CycleId(cycleId);
        var index = await _cases.GetAsync(cycle, urn, cancellationToken);
        var state = await _states.LoadLatestStateAsync(cycle, urn, cancellationToken);
        var gates = await _gates.ListAsync(cycle, urn, cancellationToken);
        if (index is null && state is null)
            return NotFound();

        var a4 = state is null ? null : A4ChatSafeAssembler.FromRecoveryState(state);
        return Ok(new CaseDetailDto(
            cycleId,
            dealerUrn,
            index?.Status ?? state!.Status.ToString(),
            index?.WaitingGate ?? state?.WaitingGate?.ToString(),
            state?.TerminationReason,
            state?.NoticeVerdict?.Decision.ToString(),
            a4?.Eligible ?? state?.Eligibility?.Eligible,
            a4?.ClockStatus ?? state?.Clock?.Status.ToString(),
            a4?.DaysRemaining ?? state?.Clock?.DaysRemaining,
            gates.Select(g => new GateAuditDto(
                g.Gate.ToString(), g.ActorUpn, g.ActorRole.ToString(), g.Decision.ToString(), g.Reason, g.DecidedUtc)).ToList(),
            a4?.SelectedChequeNumber,
            a4?.SelectedChequeStatus,
            a4?.MemoReasonCode,
            a4?.NoticeByDate,
            a4?.CureEndsDate,
            a4?.FileByDate,
            a4?.Provenance.Status,
            a4?.DataCompleteness,
            a4?.TbcIndicators,
            a4?.ProductionLegalBlockedOnApprovedStoredProcedure ?? true));
    }
}
