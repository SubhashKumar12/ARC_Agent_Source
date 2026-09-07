namespace ARC.Knowledge.Embeddings;

/// <summary>
/// Embedding provider for queries and document ingestion.
/// Implementations must not live in agents or workflows.
/// </summary>
public interface IEmbeddingProvider
{
    bool IsAvailable { get; }

    int Dimensions { get; }

    string ModelId { get; }

    /// <summary>
    /// Embeds a single query text for retrieval.
    /// </summary>
    Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Batch embeds multiple texts for document ingestion. Phase 2+.
    /// </summary>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        CancellationToken cancellationToken = default);
}
