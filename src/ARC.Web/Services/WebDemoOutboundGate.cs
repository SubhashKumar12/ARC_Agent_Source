using ARC.Agents.Workflows.Outbound;
using ARC.Domain.Workflow;
using ARC.Tools.Field;

namespace ARC.Web.Services;

/// <summary>
/// Shadow Mode outbound gate for web demo.
/// Records intended actions but never dispatches them.
/// </summary>
public sealed class WebDemoOutboundGate : IOutboundGate
{
    private readonly object _gate = new();
    private readonly List<OutboundAction> _actions = [];

    public IReadOnlyList<OutboundAction> RecordedActions
    {
        get
        {
            lock (_gate)
                return [.. _actions];
        }
    }

    public void Clear()
    {
        lock (_gate)
            _actions.Clear();
    }

    public Task OnVisitPlannedAsync(VisitTask visit, RecoveryState state, CancellationToken cancellationToken = default)
    {
        var action = new OutboundAction("FieldVisit", $"{state.DealerUrn.Value}: Visit {visit.TaskId}");
        lock (_gate)
            _actions.Add(action);
        return Task.CompletedTask;
    }

    public Task OnNoticeReadyAsync(RecoveryState state, CancellationToken cancellationToken = default)
    {
        var action = new OutboundAction("Notice", $"{state.DealerUrn.Value}: Notice ready for dealer");
        lock (_gate)
            _actions.Add(action);
        return Task.CompletedTask;
    }
}

public sealed record OutboundAction(string Type, string Summary);
