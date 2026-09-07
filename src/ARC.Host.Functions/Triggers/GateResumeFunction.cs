using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ARC.Agents.Workflows.Models;
using ARC.Data.Serialization;
using ARC.Host.Functions.Runtime;

namespace ARC.Host.Functions.Triggers;

public sealed class GateResumeFunction
{
    private readonly DealerWorkflowRunner _runner;
    private readonly ILogger<GateResumeFunction> _logger;

    public GateResumeFunction(DealerWorkflowRunner runner, ILogger<GateResumeFunction> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    [Function(nameof(GateResumeFunction))]
    public async Task Run(
        [ServiceBusTrigger("%GateResumeQueue%", Connection = "ServiceBusConnection")] string body,
        CancellationToken cancellationToken)
    {
        var request = ArcJson.Deserialize<GateResumeRequest>(body);
        _logger.LogInformation(
            "Gate resume cycle {CycleId} dealer {DealerUrn} kind {Kind} decision {Decision}",
            request.CycleId, request.DealerUrn, request.Kind, request.Decision);
        await _runner.ResumeAsync(request, cancellationToken);
    }
}
