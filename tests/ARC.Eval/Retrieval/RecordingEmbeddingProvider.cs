using ARC.Knowledge.Embeddings;

namespace ARC.Eval.Retrieval;

/// <summary>
/// Decorator over a production <see cref="IEmbeddingProvider"/>. Counts query embeds; does not change vectors.
/// </summary>
public sealed class RecordingEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingProvider _inner;

    public RecordingEmbeddingProvider(IEmbeddingProvider inner)
    {
        _inner = inner;
        InnerTypeName = inner.GetType().Name;
    }

    public string InnerTypeName { get; }

    public bool IsAvailable => _inner.IsAvailable;

    public int Dimensions => _inner.Dimensions;

    public string ModelId => _inner.ModelId;

    public int EmbedQueryCalls { get; private set; }

    public void ResetCounters() => EmbedQueryCalls = 0;

    public async Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        EmbedQueryCalls++;
        return await _inner.EmbedQueryAsync(text, cancellationToken);
    }

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        CancellationToken cancellationToken = default)
        => _inner.GenerateEmbeddingsAsync(inputs, cancellationToken);
}
