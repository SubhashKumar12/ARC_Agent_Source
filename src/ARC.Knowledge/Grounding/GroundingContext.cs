using ARC.Knowledge.Provenance;

namespace ARC.Knowledge.Grounding;

/// <summary>Compact authoritative structured fact from deterministic graph/SQL context.</summary>
public sealed record StructuredFact(
    string FactId,
    string Kind,
    string Label,
    string Summary,
    EvidenceTrustLevel TrustLevel,
    SourceReference Provenance);

/// <summary>Diagnostics for grounding calls. Not prompt content.</summary>
public sealed record GroundingDiagnostics(
    GroundingPurpose Purpose,
    string? DocumentCategory,
    string? DocumentType,
    int GraphFactCount,
    int KnowledgePromptChunkCount,
    int RetrievalTopK,
    int MaxPromptChunks,
    bool InsufficientEvidence,
    string? Notes);

/// <summary>
/// Purpose-built grounded context. Structured facts and reference knowledge stay distinct.
/// </summary>
public sealed record GroundingContext(
    IReadOnlyList<StructuredFact> StructuredFacts,
    IReadOnlyList<EvidenceSource> KnowledgeChunks,
    IReadOnlyList<SourceReference> Citations,
    GroundingDiagnostics Diagnostics,
    DateTimeOffset RetrievedUtc)
{
    public bool HasEvidence => StructuredFacts.Count > 0 || KnowledgeChunks.Count > 0;
}
