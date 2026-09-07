using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ARC.Agents.Workflows.Models;
using ARC.Api.Auth;
using ARC.Api.DTOs;
using ARC.Data.Messaging;
using ARC.Data.Serialization;
using ARC.Data.Sql;
using ARC.Domain.ValueObjects;
using ARC.Api.Configuration;

namespace ARC.Api.Controllers;

[ApiController]
[Route("v1/cycles/{cycleId}/dealers/{dealerUrn}/runs")]
public sealed class RunsController : ControllerBase
{
    private readonly IDealerRepository _dealers;
    private readonly IServiceBusPublisher _bus;
    private readonly ArcApiOptions _options;

    public RunsController(IDealerRepository dealers, IServiceBusPublisher bus, IOptions<ArcApiOptions> options)
    {
        _dealers = dealers;
        _bus = bus;
        _options = options.Value;
    }

    [HttpPost]
    public async Task<IActionResult> Start(string cycleId, string dealerUrn, [FromBody] StartRunRequest body, CancellationToken cancellationToken)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        if (!GateAccess.CanStartRun(actor))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Only Finance or Depot Admin can start a run." });

        var dealer = await _dealers.GetAsync(new DealerUrn(dealerUrn), cancellationToken);
        if (dealer is null)
            return NotFound();
        if (!GateAccess.CanReadDealer(actor, dealer.Region, dealer.Depot))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Dealer is outside the actor's region or depot." });

        var asOf = body.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new WorkflowRunRequest
        {
            CycleId = cycleId,
            DealerUrn = dealerUrn,
            AsOf = asOf,
            CorrelationId = CorrelationId.New().Value,
            Mode = body.Mode ?? _options.DefaultRunMode,
            Kind = body.Kind
        };

        await _bus.PublishCycleFanOutAsync(
            ArcJson.Serialize(request),
            $"{cycleId}|{dealerUrn}|{body.Kind}",
            cancellationToken);
        return Accepted(new { status = "queued", request.Kind, request.Mode });
    }
}
