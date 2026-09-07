using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ARC.Agents.Workflows.Models;
using ARC.Data.Serialization;
using ARC.Host.Functions.Runtime;

namespace ARC.Host.Functions.Triggers;

/// <summary>Internal resume endpoint. The public human-approval API belongs in ARC.Api.</summary>
public sealed class GateResumeHttpFunction
{
    private readonly DealerWorkflowRunner _runner;
    private readonly ILogger<GateResumeHttpFunction> _logger;

    public GateResumeHttpFunction(DealerWorkflowRunner runner, ILogger<GateResumeHttpFunction> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    [Function(nameof(GateResumeHttpFunction))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "gates/resume")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);
        var payload = ArcJson.Deserialize<GateResumeRequest>(body);
        _logger.LogInformation(
            "HTTP gate resume cycle {CycleId} dealer {DealerUrn} kind {Kind}",
            payload.CycleId, payload.DealerUrn, payload.Kind);
        await _runner.ResumeAsync(payload, cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteStringAsync("resumed", cancellationToken);
        return response;
    }
}
