namespace ARC.Knowledge.Retrieval;

/// <summary>
/// Server-side retrieval scope. Region and dealer must come from the actor / resolved identity, not the model.
/// </summary>
public sealed record RetrievalAuthorization(string? ActorRegion, string? DealerUrn);

public static class RetrievalScope
{
    private static readonly AsyncLocal<RetrievalAuthorization?> CurrentValue = new();

    public static RetrievalAuthorization? Current => CurrentValue.Value;

    public static IDisposable Enter(RetrievalAuthorization authorization)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = authorization;
        return new Pop(previous);
    }

    private sealed class Pop : IDisposable
    {
        private readonly RetrievalAuthorization? _previous;
        private bool _disposed;

        public Pop(RetrievalAuthorization? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
                return;
            CurrentValue.Value = _previous;
            _disposed = true;
        }
    }
}
