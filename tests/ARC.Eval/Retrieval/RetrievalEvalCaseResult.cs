namespace ARC.Eval.Retrieval;

public sealed record RetrievalEvalHit(
    int Rank,
    string LogicalKey,
    string DocumentId,
    string? SourceDocumentId,
    string? Version,
    string? PageOrSection,
    string? BlobLocation,
    string Status,
    string? RegionScope,
    string? DealerUrn,
    double? Score,
    string RetrievalKind);

public sealed record RetrievalEvalCaseResult(
    string CaseId,
    RetrievalEvalCategory Category,
    string ForcedMode,
    string ObservedKind,
    IReadOnlyList<RetrievalEvalHit> Hits,
    bool HasRelevantHit,
    int? FirstRelevantRank,
    bool MetadataViolation,
    string? MetadataViolationReason,
    bool CitationComplete,
    bool NegativeFalsePositive,
    int EmbeddingCalls,
    long ElapsedMilliseconds);
