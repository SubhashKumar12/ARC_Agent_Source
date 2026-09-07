namespace ARC.Knowledge.Ingestion;

/// <summary>
/// Observability metrics for document ingestion process.
/// </summary>
public sealed record IngestionResult
{
    public int DocumentsProcessed { get; init; }
    public int ChunksProduced { get; init; }
    public int ChunksEmbedded { get; init; }
    public int ChunksSkippedUnchanged { get; init; }
    public int ChunksFailed { get; init; }
    public TimeSpan Duration { get; init; }
    public List<string> Errors { get; init; } = new();

    public bool IsSuccess => ChunksFailed == 0 && Errors.Count == 0;

    public override string ToString()
    {
        return $"Ingestion Result: {DocumentsProcessed} documents, {ChunksProduced} chunks " +
               $"({ChunksEmbedded} embedded, {ChunksSkippedUnchanged} skipped, {ChunksFailed} failed) " +
               $"in {Duration.TotalSeconds:F2}s";
    }
}

/// <summary>
/// Builder for accumulating ingestion metrics.
/// </summary>
internal sealed class IngestionResultBuilder
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private int _documentsProcessed;
    private int _chunksProduced;
    private int _chunksEmbedded;
    private int _chunksSkipped;
    private int _chunksFailed;
    private readonly List<string> _errors = new();

    public void IncrementDocumentsProcessed() => _documentsProcessed++;
    public void IncrementChunksProduced(int count) => _chunksProduced += count;
    public void IncrementChunksEmbedded() => _chunksEmbedded++;
    public void IncrementChunksSkipped() => _chunksSkipped++;
    public void IncrementChunksFailed() => _chunksFailed++;
    public void AddError(string error) => _errors.Add(error);

    public IngestionResult Build()
    {
        return new IngestionResult
        {
            DocumentsProcessed = _documentsProcessed,
            ChunksProduced = _chunksProduced,
            ChunksEmbedded = _chunksEmbedded,
            ChunksSkippedUnchanged = _chunksSkipped,
            ChunksFailed = _chunksFailed,
            Duration = DateTimeOffset.UtcNow - _startTime,
            Errors = _errors
        };
    }
}
