using Microsoft.AspNetCore.Mvc;
using ARC.Api.Auth;
using ARC.Api.DTOs;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;
using ARC.Tools.Field;

namespace ARC.Api.Controllers;

/// <summary>
/// TSI confirmation of a Speech/PTP candidate. Not a G1–G4 RequestPort and not an MCP write tool.
/// </summary>
[ApiController]
[Route("v1/ptp")]
public sealed class PtpController : ControllerBase
{
    private readonly PtpCaptureOrchestrator _orchestrator;
    private readonly IPtpCandidateStore _candidates;
    private readonly IDealerRepository _dealers;
    private readonly ILogger<PtpController> _logger;

    public PtpController(
        PtpCaptureOrchestrator orchestrator,
        IPtpCandidateStore candidates,
        IDealerRepository dealers,
        ILogger<PtpController> logger)
    {
        _orchestrator = orchestrator;
        _candidates = candidates;
        _dealers = dealers;
        _logger = logger;
    }

    [HttpPost("confirmations")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmPtpRequest body, CancellationToken cancellationToken)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        if (actor.Role != ActorRole.Tsi)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Only a TSI actor may confirm a Promise-to-Pay candidate." });
        if (string.IsNullOrWhiteSpace(body.RecordId))
            return BadRequest(new { error = "RecordId is required." });

        try
        {
            var stored = await _candidates.GetAsync(body.RecordId, cancellationToken);
            if (stored is not null)
            {
                var dealer = await _dealers.GetAsync(new DealerUrn(stored.DealerUrn), cancellationToken);
                if (dealer is not null && !GateAccess.CanReadDealer(actor, dealer.Region, dealer.Depot))
                    return StatusCode(StatusCodes.Status403Forbidden, new { error = "Dealer is outside the actor's region or depot." });
            }

            var result = await _orchestrator.ConfirmAsync(
                new PtpConfirmRequest(
                    body.RecordId,
                    new FieldActor(actor.Upn, actor.Role, actor.Region, actor.Depot),
                    body.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    body.CommitmentDate,
                    body.Amount,
                    HttpContext.TraceIdentifier),
                cancellationToken);

            _logger.LogInformation(
                "PTP confirmed record {RecordId} dealer {DealerUrn} cycle {CycleId} actor {Actor}",
                result.Candidate.RecordId,
                result.Candidate.DealerUrn,
                result.Candidate.CycleId,
                actor.Upn);

            return Ok(new
            {
                recordId = result.Candidate.RecordId,
                dealerUrn = result.Candidate.DealerUrn,
                cycleId = result.Candidate.CycleId,
                confirmedByTsi = result.Committed.ConfirmedByTsi,
                status = result.Candidate.Status.ToString()
            });
        }
        catch (ToolException ex)
        {
            if (ex.Message.Contains("outside the actor", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("Only a TSI", StringComparison.OrdinalIgnoreCase))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
            return BadRequest(new { error = ex.Message });
        }
    }
}
