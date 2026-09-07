using System.Collections.Concurrent;

namespace ARC.Knowledge.Documents;

/// <summary>
/// Process-local extraction cache. Sufficient for CLI/Web/tests and in-process Azure runs.
/// Durable Cosmos persistence is a later Azure DEV item.
/// </summary>
public sealed class InMemoryDocumentExtractionStore : IDocumentExtractionStore
{
    private readonly ConcurrentDictionary<string, PersistedDocumentExtraction> _items = new(StringComparer.Ordinal);

    public Task<PersistedDocumentExtraction?> GetAsync(string contentHash, string modelId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(contentHash) || string.IsNullOrWhiteSpace(modelId))
            return Task.FromResult<PersistedDocumentExtraction?>(null);
        _items.TryGetValue(Key(contentHash, modelId), out var found);
        return Task.FromResult(found);
    }

    public Task SaveAsync(PersistedDocumentExtraction extraction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(extraction);
        _items[Key(extraction.ContentHash, extraction.ModelId)] = extraction;
        return Task.CompletedTask;
    }

    internal static string Key(string contentHash, string modelId) => $"{contentHash}|{modelId}";
}
