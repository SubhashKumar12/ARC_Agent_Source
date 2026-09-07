using Microsoft.Extensions.Logging;
using ARC.Data.Cosmos;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;
using ARC.Tools.Exceptions;

namespace ARC.Tools.Insights;

public enum SupervisoryExceptionKind
{
    GateExpired = 0,
    WaitingForHuman = 1,
    Blocked = 2,
    Failed = 3,
    BrokenPromiseToPay = 4
}

public sealed record SupervisoryException(
    SupervisoryExceptionKind Kind,
    string DealerUrn,
    string? Detail);

public sealed record DealerInsight(
    string DealerUrn,
    string? Status,
    string? WaitingGate,
    RecoveryState? State,
    IReadOnlyList<GateDecision> Gates,
    string? Region = null,
    string? Depot = null);

public sealed record SupervisoryInsightRequest(
    string CycleId,
    DateOnly AsOf,
    string? Region,
    string? DealerUrn,
    string? CorrelationId,
    IReadOnlyList<PromiseToPay>? PromisesToPay,
    string? Depot = null);

public sealed record SupervisoryInsightResult(
    IReadOnlyList<SupervisoryException> Exceptions,
    IReadOnlyList<DealerInsight> Dealers,
    IReadOnlyList<RankedWorklistEntry> Worklist);

/// <summary>
/// Exception queue for A8 (ASM/HO). Not a BI platform. Does not approve any gate.
/// Lever-effectiveness analytics are omitted — no outcome store exists in this layer.
/// </summary>
public sealed class SupervisoryInsightTool
{
    public const string Name = "GetSupervisoryInsights";

    private readonly IDealerRepository _dealers;
    private readonly IRecoveryCaseRepository _cases;
    private readonly IWorkflowStateRepository _workflow;
    private readonly IGateDecisionRepository _gates;
    private readonly ILogger<SupervisoryInsightTool> _logger;

    public SupervisoryInsightTool(
        IDealerRepository dealers,
        IRecoveryCaseRepository cases,
        IWorkflowStateRepository workflow,
        IGateDecisionRepository gates,
        ILogger<SupervisoryInsightTool> logger)
    {
        _dealers = dealers;
        _cases = cases;
        _workflow = workflow;
        _gates = gates;
        _logger = logger;
    }

    public async Task<SupervisoryInsightResult> GetAsync(
        SupervisoryInsightRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(request.CycleId))
            throw new ToolException(Name, "CycleId is required.");
        if (string.IsNullOrWhiteSpace(request.DealerUrn) && string.IsNullOrWhiteSpace(request.Region))
            throw new ToolException(Name, "Provide DealerUrn or Region.");

        var cycle = new CycleId(request.CycleId);

        try
        {
            IReadOnlyList<Dealer> dealers;
            if (!string.IsNullOrWhiteSpace(request.DealerUrn))
            {
                var one = await _dealers.GetAsync(new DealerUrn(request.DealerUrn), cancellationToken)
                    ?? throw new ToolException(Name, $"Dealer '{request.DealerUrn}' was not found.");
                if (!string.IsNullOrWhiteSpace(request.Depot)
                    && !string.Equals(one.Depot, request.Depot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ToolException(Name, "Dealer is outside the authorized depot.");
                }

                dealers = [one];
            }
            else
            {
                dealers = await _dealers.ListByRegionAsync(request.Region!, cancellationToken);
                if (!string.IsNullOrWhiteSpace(request.Depot))
                {
                    dealers = dealers
                        .Where(d => string.Equals(d.Depot, request.Depot, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
            }

            var insights = new List<DealerInsight>();
            var exceptions = new List<SupervisoryException>();

            foreach (var dealer in dealers)
            {
                var index = await _cases.GetAsync(cycle, dealer.Urn, cancellationToken);
                var state = await _workflow.LoadLatestStateAsync(cycle, dealer.Urn, cancellationToken);
                var gates = await _gates.ListAsync(cycle, dealer.Urn, cancellationToken);

                insights.Add(new DealerInsight(
                    dealer.Urn.Value,
                    index?.Status ?? state?.Status.ToString(),
                    index?.WaitingGate ?? state?.WaitingGate?.ToString(),
                    state,
                    gates,
                    dealer.Region,
                    dealer.Depot));

                if (gates.Any(g => g.Decision == GateDecisionStatus.Expired))
                    exceptions.Add(new SupervisoryException(SupervisoryExceptionKind.GateExpired, dealer.Urn.Value, "A human gate expired."));

                if (state?.Status == WorkflowStatus.WaitingForHuman || !string.IsNullOrWhiteSpace(index?.WaitingGate))
                    exceptions.Add(new SupervisoryException(SupervisoryExceptionKind.WaitingForHuman, dealer.Urn.Value, index?.WaitingGate ?? state?.WaitingGate?.ToString()));

                if (state?.Status == WorkflowStatus.Blocked)
                    exceptions.Add(new SupervisoryException(SupervisoryExceptionKind.Blocked, dealer.Urn.Value, state.TerminationReason));

                if (state?.Status == WorkflowStatus.Failed)
                    exceptions.Add(new SupervisoryException(SupervisoryExceptionKind.Failed, dealer.Urn.Value, state.TerminationReason));
            }

            if (request.PromisesToPay is { Count: > 0 })
            {
                foreach (var ptp in request.PromisesToPay)
                {
                    if (request.AsOf > ptp.CommitmentDate)
                        exceptions.Add(new SupervisoryException(
                            SupervisoryExceptionKind.BrokenPromiseToPay,
                            ptp.DealerUrn.Value,
                            $"Commitment {ptp.CommitmentDate:yyyy-MM-dd} missed."));
                }
            }

            var worklist = string.IsNullOrWhiteSpace(request.DealerUrn) || !string.IsNullOrWhiteSpace(request.Region)
                ? (await _cases.ListRankedWorklistAsync(
                    cycle, request.Region, request.Depot, topDecile: false, cancellationToken)).Entries
                : [];

            _logger.LogInformation(
                "Tool {Tool} cycle {CycleId} correlation {CorrelationId} dealers {DealerCount} exceptions {ExceptionCount} worklist {WorklistCount} durationMs {DurationMs}",
                Name, request.CycleId, request.CorrelationId, insights.Count, exceptions.Count, worklist.Count,
                (DateTimeOffset.UtcNow - started).TotalMilliseconds);

            return new SupervisoryInsightResult(exceptions, insights, worklist);
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(Name, "Failed to load supervisory data.", ex);
        }
    }
}
