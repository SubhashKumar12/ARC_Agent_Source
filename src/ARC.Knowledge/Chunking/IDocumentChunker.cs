namespace ARC.Knowledge.Chunking;

/// <summary>
/// Abstraction for document chunking strategies. Implementations must be deterministic and idempotent.
/// </summary>
public interface IDocumentChunker
{
    /// <summary>
    /// Chunks the source document into searchable units preserving metadata.
    /// Must be deterministic: same input produces same chunks.
    /// </summary>
    IReadOnlyList<ChunkResult> ChunkDocument(SourceDocument document);

    /// <summary>
    /// Document categories this chunker can handle.
    /// </summary>
    IReadOnlySet<string> SupportedCategories { get; }
}
