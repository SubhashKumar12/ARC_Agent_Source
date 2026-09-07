using Microsoft.Extensions.Options;
using ARC.Knowledge.Configuration;

namespace ARC.Knowledge.Embeddings;

/// <summary>Used when no embedding deployment is configured. Vector queries are skipped.</summary>
public sealed class DisabledEmbeddingProvider : IEmbeddingProvider
{
    public DisabledEmbeddingProvider(IOptions<ArcKnowledgeOptions> options)
        : this(options.Value.Embeddings)
    {
    }

    public DisabledEmbeddingProvider(EmbeddingProviderOptions embeddings)
    {
        Dimensions = embeddings.Dimensions > 0 ? embeddings.Dimensions : 3072;
        ModelId = string.IsNullOrWhiteSpace(embeddings.Deployment) ? "disabled" : embeddings.Deployment;
    }

    public bool IsAvailable => false;

    public int Dimensions { get; }

    public string ModelId { get; }

    public Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
        => Task.FromResult<float[]?>(null);

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<float[]>>(Array.Empty<float[]>());
}
