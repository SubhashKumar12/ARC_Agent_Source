using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ARC.Agents.Workflows.Models;
using ARC.Data.Serialization;
using ARC.Host.Functions.Runtime;

namespace ARC.Host.Functions.Triggers;

public sealed class CycleFanOutFunction
{
    private readonly DealerWorkflowRunner _runner;
    private readonly ILogger<CycleFanOutFunction> _logger;

    public CycleFanOutFunction(DealerWorkflowRunner runner, ILogger<CycleFanOutFunction> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    [Function(nameof(CycleFanOutFunction))]
    public async Task Run(
        [ServiceBusTrigger("%CycleFanOutQueue%", Connection = "ServiceBusConnection")] string body,
        CancellationToken cancellationToken)
    {
        var request = ArcJson.Deserialize<WorkflowRunRequest>(body);
        _logger.LogInformation(
            "Dealer workflow start cycle {CycleId} dealer {DealerUrn} kind {Kind} mode {Mode}",
            request.CycleId, request.DealerUrn, request.Kind, request.Mode);
        await _runner.RunAsync(request, cancellationToken);
    }
}
