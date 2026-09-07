using Microsoft.AspNetCore.Mvc;
using ARC.Agents.Workflows.Models;
using ARC.Api.Auth;
using ARC.Api.DTOs;
using ARC.Api.Services;
using ARC.Data.Cosmos;
using ARC.Data.Messaging;
using ARC.Data.Serialization;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Api.Controllers;

[ApiController]
[Route("v1/gates")]
public sealed class GatesController : ControllerBase
{
    private readonly IDealerRepository _dealers;
    private readonly IConversationStateRepository _pending;
    private readonly IServiceBusPublisher _bus;
    private readonly IAuditRepository _audit;
    private readonly ILogger<GatesController> _logger;

    public GatesController(
        IDealerRepository dealers,
        IConversationStateRepository pending,
        IServiceBusPublisher bus,
        IAuditRepository audit,
        ILogger<GatesController> logger)
    {
        _dealers = dealers;
        _pending = pending;
        _bus = bus;
        _audit = audit;
        _logger = logger;
    }

    [HttpPost("{gateId}/decisions")]
    public async Task<IActionResult> Decide(string gateId, [FromBody] GateDecisionRequest body, CancellationToken cancellationToken)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        if (!GateCatalog.TryParse(gateId, out var gate, out var portId))
            return BadRequest(new { error = $"Unknown gate '{gateId}'." });
        if (!GateAccess.CanDecide(actor, gate, body.Decision))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This role cannot decide this gate." });
        if (string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "Decision reason is required." });

        var cycle = new CycleId(body.CycleId);
        var urn = new DealerUrn(body.DealerUrn);
        var dealer = await _dealers.GetAsync(urn, cancellationToken);
        if (dealer is null)
            return NotFound(new { error = $"Dealer '{body.DealerUrn}' was not found." });
        if (!GateAccess.CanReadDealer(actor, dealer.Region, dealer.Depot))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Dealer is outside the actor's region or depot." });

        var pendingJson = await _pending.GetAsync(cycle, urn, cancellationToken);
        if (string.IsNullOrWhiteSpace(pendingJson))
            return Conflict(new { error = "No pending human gate for this dealer in this cycle." });

        PendingGateHalt pending;
        try
        {
            pending = ArcJson.Deserialize<PendingGateHalt>(pendingJson);
        }
        catch (Exception)
        {
            return Conflict(new { error = "Pending gate payload is not resumable." });
        }

        if (!string.Equals(pending.PortId, portId, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = $"Pending gate is '{pending.PortId}', not '{portId}'." });
        if (body.Kind is { } kind && kind != pending.Kind)
            return Conflict(new { error = $"Pending workflow is {pending.Kind}." });

        var resume = new GateResumeRequest
        {
            CycleId = body.CycleId,
            DealerUrn = body.DealerUrn,
            Kind = pending.Kind,
            ActorUpn = actor.Upn,
            ActorRole = actor.Role,
            Decision = body.Decision,
            Reason = body.Reason
        };

        await _bus.PublishGateResumeAsync(
            ArcJson.Serialize(resume),
            $"{portId}|{body.CycleId}|{body.DealerUrn}",
            cancellationToken);
        await _audit.AppendAsync(
            new AuditEvent("api_gate_decision", body.CycleId, body.DealerUrn, pending.RequestId, DateTimeOffset.UtcNow, $"{portId}:{body.Decision}"),
            cancellationToken);

        _logger.LogInformation(
            "Gate decision published gate {Gate} cycle {CycleId} dealer {DealerUrn} actor {Actor} decision {Decision}",
            portId, body.CycleId, body.DealerUrn, actor.Upn, body.Decision);
        return Accepted(new { status = "queued", gate = portId, pending.Kind });
    }
}
