using ARC.Knowledge.Retrieval;

namespace ARC.Eval.Retrieval.Live;

public sealed record LiveAzureVerification(
    bool DocumentsContainerReachable,
    int VectorContractDimensions,
    int EmbeddingProviderDimensions,
    int? ObservedQueryEmbeddingDimensions,
    bool VectorRetrievalExecuted,
    bool LexicalRetrievalExecuted,
    bool MetadataFilterExecuted,
    bool TopKCapEnforced,
    string Notes);

public sealed record LiveRetrievalEvalOutcome(
    string Status,
    RetrievalEvalReport? Report,
    LiveAzureVerification? Azure,
    string CitationFaithfulness,
    IReadOnlyList<string> RouteDiagnostics,
    string Notes)
{
    public const string PendingStatus = "LIVE RETRIEVAL VERIFICATION PENDING";
    public const string RanStatus = "LIVE";
    public const string FailedClosedStatus = "FAILED CLOSED";
    public const string CitationFaithfulnessNotMeasured = "NOT MEASURED";

    public static LiveRetrievalEvalOutcome Pending(string reason)
        => new(
            PendingStatus,
            Report: null,
            Azure: null,
            CitationFaithfulnessNotMeasured,
            [],
            reason);

    public bool IsPending => string.Equals(Status, PendingStatus, StringComparison.Ordinal);
}
