using ARC.Domain.Architecture;

namespace ARC.Domain.Tests;

public sealed class ProductionDataPlacementTests
{
    [Fact]
    public void Source_precedence_is_ordered_and_semantic_cannot_override_sql_or_rules()
    {
        Assert.Equal(
            [
                ProductionDataPlacement.SourcePrecedence.DeterministicDomainOrRule,
                ProductionDataPlacement.SourcePrecedence.ApprovedSqlStoredProcedure,
                ProductionDataPlacement.SourcePrecedence.PersistedWorkflowStateFromTools,
                ProductionDataPlacement.SourcePrecedence.GovernedDocumentOrEvidence,
                ProductionDataPlacement.SourcePrecedence.SemanticRetrieval,
                ProductionDataPlacement.SourcePrecedence.LlmNarration
            ],
            ProductionDataPlacement.PrecedenceOrder);

        Assert.False(ProductionDataPlacement.SemanticMayOverride(
            ProductionDataPlacement.SourcePrecedence.DeterministicDomainOrRule));
        Assert.False(ProductionDataPlacement.SemanticMayOverride(
            ProductionDataPlacement.SourcePrecedence.ApprovedSqlStoredProcedure));
        Assert.False(ProductionDataPlacement.SemanticMayOverride(
            ProductionDataPlacement.SourcePrecedence.PersistedWorkflowStateFromTools));
        Assert.True(ProductionDataPlacement.SemanticMayOverride(
            ProductionDataPlacement.SourcePrecedence.SemanticRetrieval));
        Assert.True(ProductionDataPlacement.SemanticMayOverride(
            ProductionDataPlacement.SourcePrecedence.LlmNarration));
    }

    [Fact]
    public void Transactional_numeric_facts_must_not_be_embedded()
    {
        Assert.Contains("outstanding amount", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("net recoverable exposure", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("cheque amount", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("cheque date", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("business-line limit", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("eligibility verdict", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("statutory dates", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("PTP commitment date", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("gate status", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("workflow status", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.Contains("ageing bucket amounts", ProductionDataPlacement.ForbiddenToEmbed);
        Assert.DoesNotContain(ProductionDataPlacement.ForbiddenToEmbed, f =>
            ProductionDataPlacement.AllowedSemanticText.Contains(f, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Exact_business_questions_route_to_tools_not_embeddings()
    {
        Assert.Equal(
            ProductionDataPlacement.ChatRetrievalPath.ExactBusinessFact,
            ProductionDataPlacement.PathForQuestion(needsExactBusinessFact: true, needsSemanticReference: false));
        Assert.Equal(
            ProductionDataPlacement.ChatRetrievalPath.SemanticReference,
            ProductionDataPlacement.PathForQuestion(needsExactBusinessFact: false, needsSemanticReference: true));
        Assert.Equal(
            ProductionDataPlacement.ChatRetrievalPath.Combined,
            ProductionDataPlacement.PathForQuestion(needsExactBusinessFact: true, needsSemanticReference: true));
    }

    [Fact]
    public void Cosmos_operational_containers_do_not_include_documents()
    {
        Assert.Equal(
            ["cycleState", "checkpoints", "auditEvents", "conversationState"],
            ProductionDataPlacement.CosmosOperationalContainers);
        Assert.Equal("documents", ProductionDataPlacement.CosmosKnowledgeContainer);
        Assert.DoesNotContain("documents", ProductionDataPlacement.CosmosOperationalContainers);
        Assert.Equal(["evidence", "legal-worm"], ProductionDataPlacement.BlobContainers);
    }
}
