namespace ARC.Eval.Retrieval;

/// <summary>
/// One labelled retrieval query. Ground truth is fixture metadata — never LLM-judged.
/// </summary>
public sealed record RetrievalEvalCase(
    string CaseId,
    string Query,
    RetrievalEvalCategory Category,
    IReadOnlyList<string> RelevantLogicalKeys,
    string? ExpectedDocumentType = null,
    string? ExpectedDocumentCategory = null,
    string? ExpectedVersion = null,
    string? ExpectedRegion = null,
    string? ExpectedDealerUrn = null,
    bool ExpectNoRelevantHit = false,
    string Notes = "");
