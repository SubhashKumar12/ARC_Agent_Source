namespace ARC.Knowledge.Retrieval;

public sealed record DocumentRetrievalFilter(
    string Status,
    string Version,
    string? Region,
    string? DealerUrn,
    string? DocumentCategory,
    string? DocumentType);

public sealed record DocumentRetrievalRequest(
    string Text,
    float[]? QueryEmbedding,
    DocumentRetrievalFilter Filter,
    RetrievalQueryKind Kind,
    int TopK);
