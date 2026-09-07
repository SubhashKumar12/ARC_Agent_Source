using Microsoft.Extensions.Logging;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;
using ARC.Tools.Persistence;
using System.Diagnostics;

namespace ARC.Tools.Field;

/// <summary>
/// Deterministic TSI-territory visit plan assembler.
/// Groups by CoveringTsi → Region → Depot; sequences by A2 score DESC then DealerUrn ASC.
/// No lat/long, no Maps, no k-means, no LLM clustering.
/// </summary>
public sealed class VisitPlanAssembler
{
    public const string Name = "AssembleVisitPlan";

    private readonly IDealerRepository _dealers;
    private readonly IVisitPlanStore _plans;
    private readonly ILogger<VisitPlanAssembler> _logger;

    public VisitPlanAssembler(
        IDealerRepository dealers,
        IVisitPlanStore plans,
        ILogger<VisitPlanAssembler> logger)
    {
        _dealers = dealers;
        _plans = plans;
        _logger = logger;
    }

    /// <summary>
    /// Assemble grouped TSI visit plans from existing visit tasks and A2 scores.
    /// Input: visit tasks (already created by A6 PlanVisit).
    /// Output: TSI-grouped plans with deterministic sequence.
    /// </summary>
    public async Task<IReadOnlyList<VisitPlan>> AssemblePlansAsync(
        CycleId cycleId,
        DateOnly asOf,
        IReadOnlyList<VisitTask> visitTasks,
        IReadOnlyDictionary<string, decimal> recoverabilityScores,
        IReadOnlyDictionary<string, bool> brokenPtpFlags,
        CorrelationId correlationId,
        RunMode mode,
        CancellationToken cancellationToken)
    {
        using var activity = new ActivitySource("ARC").StartActivity("arc.visit.plan");
        activity?.SetTag("cycle_id", cycleId.Value);
        activity?.SetTag("correlation_id", correlationId.Value);
        activity?.SetTag("mode", mode.ToString());

        var started = DateTimeOffset.UtcNow;

        try
        {
            // Load all dealers
            var dealers = await _dealers.ListAllAsync(cancellationToken);
            var dealerMap = dealers.ToDictionary(d => d.Urn.Value, StringComparer.Ordinal);

            // Group by CoveringTsi
            var tsiGroups = visitTasks
                .GroupBy(v => v.CoveringTsi, StringComparer.Ordinal)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToList();

            var plans = new List<VisitPlan>();

            foreach (var tsiGroup in tsiGroups)
            {
                var tsiId = tsiGroup.Key!;
                var planId = $"{cycleId.Value}|{tsiId}";

                // Further group by region and depot for cluster ID
                var lines = new List<VisitPlanLine>();
                var regionDepotGroups = tsiGroup
                    .GroupBy(v => (v.Region, v.Depot))
                    .ToList();

                foreach (var clusterGroup in regionDepotGroups)
                {
                    var clusterId = $"{cycleId.Value}|{tsiId}|{clusterGroup.Key.Region ?? ""}|{clusterGroup.Key.Depot ?? ""}";

                    // Sort by broken PTP first, then A2 score DESC, then DealerUrn ASC
                    var orderedTasks = clusterGroup
                        .OrderByDescending(v =>
                        {
                            brokenPtpFlags.TryGetValue(v.DealerUrn, out var broken);
                            return broken ? 1 : 0;
                        })
                        .ThenByDescending(v =>
                        {
                            recoverabilityScores.TryGetValue(v.DealerUrn, out var score);
                            return score;
                        })
                        .ThenBy(v => v.DealerUrn, StringComparer.Ordinal)
                        .ToList();

                    lines.AddRange(orderedTasks.Select((task, index) =>
                    {
                        var reason = VisitPlanReason.PostNotice;
                        if (brokenPtpFlags.TryGetValue(task.DealerUrn, out var broken) && broken)
                            reason = VisitPlanReason.BrokenPtpFirst;

                        var status = mode == RunMode.Shadow ? VisitPlanStatus.SuppressedShadow : VisitPlanStatus.Planned;

                        return new VisitPlanLine
                        {
                            DealerUrn = new DealerUrn(task.DealerUrn),
                            Sequence = lines.Count + index + 1,
                            PriorityRank = lines.Count + index + 1,
                            GeoClusterId = clusterId,
                            Reason = reason,
                            Status = status,
                            VisitTaskId = task.TaskId
                        };
                    }));
                }

                if (lines.Count > 0)
                {
                    var plan = new VisitPlan
                    {
                        PlanId = planId,
                        CycleId = cycleId,
                        TsiId = tsiId,
                        PlanDate = asOf,
                        CorrelationId = correlationId,
                        Lines = lines
                    };

                    await _plans.SaveAsync(plan, cancellationToken);
                    plans.Add(plan);

                    _logger.LogInformation(
                        "Assembled visit plan {PlanId} tsi {TsiId} lines {LineCount} correlation {CorrelationId} durationMs {DurationMs}",
                        planId, tsiId, lines.Count, correlationId.Value,
                        (DateTimeOffset.UtcNow - started).TotalMilliseconds);
                }
            }

            // Handle missing CoveringTsi - fail closed
            var missingTsi = visitTasks.Where(v => string.IsNullOrWhiteSpace(v.CoveringTsi)).ToList();
            if (missingTsi.Count > 0)
            {
                _logger.LogWarning(
                    "Visit plan assembly: {Count} dealers missing CoveringTsi - fail closed, no plan created. Dealers: {Dealers}",
                    missingTsi.Count,
                    string.Join(", ", missingTsi.Select(v => v.DealerUrn)));
            }

            activity?.SetTag("status", "success");
            activity?.SetTag("plan_count", plans.Count);
            activity?.SetTag("missing_tsi_count", missingTsi.Count);

            return plans;
        }
        catch (DataAccessException ex)
        {
            activity?.SetTag("status", "error");
            _logger.LogError(ex, "Visit plan assembly failed for cycle {CycleId}", cycleId.Value);
            throw new ToolException(Name, "Failed to assemble visit plans.", ex);
        }
    }
}
