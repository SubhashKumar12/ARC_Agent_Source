using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Workflows.Models;
using ARC.Data.Messaging;
using ARC.Data.Serialization;
using ARC.Data.Sql;
using ARC.Domain.ValueObjects;
using ARC.Host.Functions;

namespace ARC.Host.Functions.Runtime;

public sealed class OdosCycleFanOut
{
    private readonly IDealerRepository _dealers;
    private readonly IServiceBusPublisher _bus;
    private readonly ArcHostOptions _options;
    private readonly ILogger<OdosCycleFanOut> _logger;

    public OdosCycleFanOut(
        IDealerRepository dealers,
        IServiceBusPublisher bus,
        IOptions<ArcHostOptions> options,
        ILogger<OdosCycleFanOut> logger)
    {
        _dealers = dealers;
        _bus = bus;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var cycleId = asOf.ToString("yyyy-MM");
        var dealers = string.IsNullOrWhiteSpace(_options.CycleRegion)
            ? await _dealers.ListAllAsync(cancellationToken)
            : await _dealers.ListByRegionAsync(_options.CycleRegion, cancellationToken);

        var published = 0;
        foreach (var dealer in dealers)
        {
            var request = new WorkflowRunRequest
            {
                CycleId = cycleId,
                DealerUrn = dealer.Urn.Value,
                AsOf = asOf,
                CorrelationId = CorrelationId.New().Value,
                Mode = _options.DefaultRunMode,
                Kind = ArcWorkflowKind.Odos
            };
            var body = ArcJson.Serialize(request);
            await _bus.PublishCycleFanOutAsync(body, $"{cycleId}|{dealer.Urn.Value}|Odos", cancellationToken);
            published++;
        }

        _logger.LogInformation("ODOS cycle {CycleId} fan-out published {Count} dealers region {Region}", cycleId, published, _options.CycleRegion ?? "*");
    }
}
