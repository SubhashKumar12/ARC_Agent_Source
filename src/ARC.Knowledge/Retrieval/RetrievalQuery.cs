namespace ARC.Knowledge.Retrieval;

public sealed record RetrievalQuery(
    string Text,
    string? DealerUrn,
    string? ActorRegion,
    string? DocumentCategory,
    string? CorrelationId,
    float[]? Embedding = null,
    int TopK = 8,
    string? DocumentType = null);
