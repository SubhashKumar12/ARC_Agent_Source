using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ARC.Host.Functions.Runtime;

namespace ARC.Host.Functions.Triggers;

public sealed class OdosMonthlyCycleFunction
{
    private readonly OdosCycleFanOut _fanOut;
    private readonly ILogger<OdosMonthlyCycleFunction> _logger;

    public OdosMonthlyCycleFunction(OdosCycleFanOut fanOut, ILogger<OdosMonthlyCycleFunction> logger)
    {
        _fanOut = fanOut;
        _logger = logger;
    }

    [Function(nameof(OdosMonthlyCycleFunction))]
    public async Task Run(
        [TimerTrigger("%OdosCycleSchedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        _logger.LogInformation("ODOS monthly cycle trigger asOf {AsOf} scheduleStatus {Status}", asOf, timer.ScheduleStatus?.Last);
        await _fanOut.PublishAsync(asOf, cancellationToken);
    }
}
