using ARC.Agents.Workflows.Outbound;
using ARC.Domain.Workflow;
using ARC.Tools.Field;

namespace ARC.Cli.Fakes;

/// <summary>Shadow outbound: records suppressed notice/visit events and never despatches.</summary>
internal sealed class CliOutboundRecorder : IOutboundGate
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
            _events.Add($"visit-suppressed:{visit.TaskId}");
        return Task.CompletedTask;
    }

    public Task OnNoticeReadyAsync(RecoveryState state, CancellationToken cancellationToken)
    {
        lock (_gate)
            _events.Add($"notice-suppressed:{state.DealerUrn.Value}");
        return Task.CompletedTask;
    }

    public void Clear()
    {
        lock (_gate)
            _events.Clear();
    }
}
