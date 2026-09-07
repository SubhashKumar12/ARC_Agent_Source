using Microsoft.Extensions.Logging;
using ARC.Domain.Enums;
using ARC.Domain.Workflow;
using ARC.Tools.Field;

namespace ARC.Agents.Workflows.Outbound;

/// <summary>
/// Central Shadow / Assisted / Live enforcement. Notices are never a model tool.
/// Shadow computes the graph but must not despatch notices or push visit tasks.
/// </summary>
public interface IOutboundGate
{
    Task OnVisitPlannedAsync(VisitTask visit, RecoveryState state, CancellationToken cancellationToken);
    Task OnNoticeReadyAsync(RecoveryState state, CancellationToken cancellationToken);
}

public sealed class ShadowOutboundGate : IOutboundGate
{
    private readonly ILogger<ShadowOutboundGate> _logger;

    public ShadowOutboundGate(ILogger<ShadowOutboundGate> logger) => _logger = logger;

    public Task OnVisitPlannedAsync(VisitTask visit, RecoveryState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "IOutboundGate Shadow suppressed visit {TaskId} dealer {DealerUrn} cycle {CycleId} mode {Mode}",
            visit.TaskId, state.DealerUrn.Value, state.CycleId.Value, state.Mode);
        return Task.CompletedTask;
    }

    public Task OnNoticeReadyAsync(RecoveryState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "IOutboundGate Shadow suppressed notice dealer {DealerUrn} cycle {CycleId} mode {Mode}",
            state.DealerUrn.Value, state.CycleId.Value, state.Mode);
        return Task.CompletedTask;
    }
}
