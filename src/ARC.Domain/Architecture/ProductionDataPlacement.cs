namespace ARC.Domain.Architecture;

/// <summary>
/// Production data-placement and Chat retrieval boundary.
/// SQL remains the structured source of record. Cosmos is not a duplicate operational database.
/// This type does not calculate amounts, dates, or eligibility.
/// </summary>
public static class ProductionDataPlacement
{
    public const string Decision =
        "Azure SQL / SQL Server is authoritative for structured operational and business facts. " +
        "Cosmos DB must not become a duplicate operational database. " +
        "Cosmos vector search is only for semantic/unstructured knowledge. " +
        "Money, dates, legal eligibility, balances, and status are tool/repository facts.";

    /// <summary>Lower rank wins. Semantic retrieval and LLM narration cannot override 1–3.</summary>
    public enum SourcePrecedence
    {
        DeterministicDomainOrRule = 1,
        ApprovedSqlStoredProcedure = 2,
        PersistedWorkflowStateFromTools = 3,
        GovernedDocumentOrEvidence = 4,
        SemanticRetrieval = 5,
        LlmNarration = 6
    }

    public enum ChatRetrievalPath
    {
        ExactBusinessFact = 1,
        SemanticReference = 2,
        Combined = 3
    }

    public static IReadOnlyList<SourcePrecedence> PrecedenceOrder { get; } =
    [
        SourcePrecedence.DeterministicDomainOrRule,
        SourcePrecedence.ApprovedSqlStoredProcedure,
        SourcePrecedence.PersistedWorkflowStateFromTools,
        SourcePrecedence.GovernedDocumentOrEvidence,
        SourcePrecedence.SemanticRetrieval,
        SourcePrecedence.LlmNarration
    ];

    /// <summary>Transactional numeric/state facts. Embed for Chat lookup is forbidden.</summary>
    public static IReadOnlyList<string> ForbiddenToEmbed { get; } =
    [
        "outstanding amount",
        "net recoverable exposure",
        "cheque amount",
        "cheque date",
        "business-line limit",
        "eligibility verdict",
        "statutory dates",
        "PTP commitment date",
        "gate status",
        "workflow status",
        "ageing bucket amounts"
    ];

    public static IReadOnlyList<string> AllowedSemanticText { get; } =
    [
        "reference documents",
        "policy/SOP text",
        "evidence/document text",
        "qualitative remarks when an approved business requirement exists",
        "governed narrative facts when explicitly approved"
    ];

    public static IReadOnlyList<string> CosmosOperationalContainers { get; } =
    [
        "cycleState",
        "checkpoints",
        "auditEvents",
        "conversationState"
    ];

    public const string CosmosKnowledgeContainer = "documents";

    public static IReadOnlyList<string> BlobContainers { get; } =
    [
        "evidence",
        "legal-worm"
    ];

    public static bool SemanticMayOverride(SourcePrecedence source)
        => (int)source >= (int)SourcePrecedence.SemanticRetrieval;

    public static ChatRetrievalPath PathForQuestion(bool needsExactBusinessFact, bool needsSemanticReference)
    {
        if (needsExactBusinessFact && needsSemanticReference)
            return ChatRetrievalPath.Combined;
        if (needsExactBusinessFact)
            return ChatRetrievalPath.ExactBusinessFact;
        return ChatRetrievalPath.SemanticReference;
    }
}
