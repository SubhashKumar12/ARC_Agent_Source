namespace ARC.Agents.Observability;

/// <summary>Flows cycle/dealer/correlation onto the model-call decorator without putting content in logs.</summary>
public sealed class ArcCallContext
{
    private static readonly AsyncLocal<ArcCallContext?> CurrentValue = new();

    public string? AgentName { get; init; }
    public string? CycleId { get; init; }
    public string? DealerUrn { get; init; }
    public string? CorrelationId { get; init; }

    public static ArcCallContext? Current => CurrentValue.Value;

    public static IDisposable Push(ArcCallContext context)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = context;
        return new Pop(previous);
    }

    private sealed class Pop : IDisposable
    {
        private readonly ArcCallContext? _previous;
        private bool _disposed;

        public Pop(ArcCallContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
                return;
            CurrentValue.Value = _previous;
            _disposed = true;
        }
    }
}
