namespace ARC.Knowledge.Chunking;

/// <summary>
/// A single chunk produced by a document chunker. Preserves all required metadata for indexing.
/// </summary>
public sealed record ChunkResult
{
    public required string Content { get; init; }
    public required string PageOrSection { get; init; }
    public required string Title { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Input document for chunking, containing raw content and metadata.
/// </summary>
public sealed record SourceDocument
{
    public required string SourceDocumentId { get; init; }
    public required string DocumentType { get; init; }
    public required string DocumentCategory { get; init; }
    public required string Content { get; init; }
    public required string Status { get; init; }
    public required string Version { get; init; }
    public required string[] RegionScope { get; init; }
    public string? DealerUrn { get; init; }
    public string? BlobLocation { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}
