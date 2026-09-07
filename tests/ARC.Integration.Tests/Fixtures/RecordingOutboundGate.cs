using ARC.Agents.Workflows.Outbound;
using ARC.Domain.Workflow;
using ARC.Tools.Field;

namespace ARC.Integration.Tests.Fixtures;

/// <summary>Shadow-equivalent outbound spy for AC#2 assertions (never despatches).</summary>
public sealed class RecordingOutboundGate : IOutboundGate
{
    private readonly List<string> _events = [];
    private readonly object _gate = new();

    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_gate)
                return [.. _events];
        }
    }

    public Task OnVisitPlannedAsync(VisitTask visit, RecoveryState state, CancellationToken cancellationToken)
    {
        lock (_gate)
            _events.Add($"visit-suppressed:{visit.TaskId}|mode={state.Mode}");
        return Task.CompletedTask;
    }

    public Task OnNoticeReadyAsync(RecoveryState state, CancellationToken cancellationToken)
    {
        lock (_gate)
            _events.Add($"notice-suppressed:{state.DealerUrn.Value}|mode={state.Mode}");
        return Task.CompletedTask;
    }
}
