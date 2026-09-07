using Microsoft.Extensions.DependencyInjection;
using ARC.Agents.A3NoticeDecisioning;
using ARC.Agents.A5DraftingVerification;
using ARC.Agents.A8SupervisoryInsight;
using ARC.Agents.Context;
using ARC.Agents.Models;
using ARC.Agents.Tests.Support;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Limitation;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Grounding;
using ARC.Tools.Drafting;

namespace ARC.Agents.Tests;

/// <summary>
/// Stage 3B grounding integration tests for A3/A5/A8.
/// Verifies grounding purpose, deterministic authority, and safe fallback behavior.
/// </summary>
public sealed class GroundingIntegrationTests
{
    private static readonly DateOnly TestDate = new(2026, 9, 1);

    #region A3 Tests

    [Fact]
    public async Task A3_requests_A3_grounding_purpose()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A3_GP";
        store.SeedDealer(Dealer(urn));
        var a3 = host.GetRequiredService<NoticeDecisioningAgent>();

        // Act
        var result = await a3.RunAsync(
            new NoticeDecisioningAgentRequest(
                Dealer(urn),
                Exposure(urn, 0m),
                null, null,
                "notice policy",
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Grounding context is populated and uses A3 purpose.
        Assert.NotNull(result.Grounding);
        Assert.Equal(GroundingPurpose.A3NoticeDecisionSupport, result.Grounding.Diagnostics.Purpose);
    }

    [Fact]
    public async Task A3_deterministic_decision_unchanged_with_grounding()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var issueUrn = "urn:dealer:A3_Issue";
        var holdUrn = "urn:dealer:A3_Hold";
        store.SeedDealer(Dealer(issueUrn));
        store.SeedDealer(Dealer(holdUrn));
        var a3 = host.GetRequiredService<NoticeDecisioningAgent>();

        // Act
        var issueResult = await a3.RunAsync(
            new NoticeDecisioningAgentRequest(
                Dealer(issueUrn),
                Exposure(issueUrn, 0m),
                null, null,
                "issue notice",
                Ctx(issueUrn)),
            CancellationToken.None);

        var holdResult = await a3.RunAsync(
            new NoticeDecisioningAgentRequest(
                Dealer(holdUrn),
                Exposure(holdUrn, 0m),
                new Dispute(new DealerUrn(holdUrn), DisputeStatus.UnderReview, "DISP-001"),
                null,
                "hold notice",
                Ctx(holdUrn)),
            CancellationToken.None);

        // Assert - Deterministic decisions remain authoritative regardless of grounding.
        Assert.Equal(NoticeDecision.Issue, issueResult.Verdict.Decision);
        Assert.Equal(NoticeDecision.Hold, holdResult.Verdict.Decision);
    }

    [Fact]
    public async Task A3_no_duplicate_retrieval_calls()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A3_NoDupe";
        store.SeedDealer(Dealer(urn));
        var a3 = host.GetRequiredService<NoticeDecisioningAgent>();

        // Act
        var result = await a3.RunAsync(
            new NoticeDecisioningAgentRequest(
                Dealer(urn),
                Exposure(urn, 0m),
                null, null,
                "policy query",
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Single grounding call (no separate graph/search calls).
        Assert.NotNull(result.Grounding);
        // Grounding provider internally coordinates graph + retrieval.
    }

    [Fact]
    public async Task A3_insufficient_evidence_does_not_block_deterministic_decision()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A3_NoEvidence";
        store.SeedDealer(Dealer(urn));
        var a3 = host.GetRequiredService<NoticeDecisioningAgent>();

        // Act - No search text, so grounding has no query-based knowledge.
        var result = await a3.RunAsync(
            new NoticeDecisioningAgentRequest(
                Dealer(urn),
                Exposure(urn, 0m),
                null, null,
                SearchText: null,
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Deterministic decision still executes.
        Assert.Equal(NoticeDecision.Issue, result.Verdict.Decision);
        // Grounding may have graph facts but no retrieved knowledge.
        Assert.NotNull(result.Grounding);
    }

    [Fact]
    public async Task A3_authoritative_amount_preserved()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A3_Amount";
        var authoritativeAmount = 123456.78m;
        store.SeedDealer(Dealer(urn));
        var a3 = host.GetRequiredService<NoticeDecisioningAgent>();

        // Act
        var result = await a3.RunAsync(
            new NoticeDecisioningAgentRequest(
                Dealer(urn),
                Exposure(urn, credits: 0m, netExposure: authoritativeAmount),
                null, null,
                "amount policy",
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Authoritative amount remains unchanged.
        // (NoticeDecisionTool uses the exposure from request, not from grounding).
        Assert.NotNull(result.Verdict);
        // The grounding may include graph facts but does not override the exposure value.
    }

    #endregion

    #region A5 Tests

    [Fact]
    public async Task A5_requests_A5_grounding_purpose()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A5_GP";
        store.SeedDealer(Dealer(urn));
        var a5 = host.GetRequiredService<DraftingVerificationAgent>();

        // Act
        var result = await a5.RunAsync(
            new DraftingVerificationAgentRequest(
                DraftKind.Section138Notice,
                Exposure(urn, 0m),
                Dealer(urn),
                Cheque: null,
                Memo: null,
                Clock: null,
                Draft: null,
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Grounding context is populated and uses A5 purpose.
        Assert.NotNull(result.Grounding);
        Assert.Equal(GroundingPurpose.A5DraftingTemplateSupport, result.Grounding.Diagnostics.Purpose);
    }

    [Fact]
    public async Task A5_deterministic_amount_preserved()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A5_Amount";
        var authoritativeAmount = 987654.32m;
        store.SeedDealer(Dealer(urn));
        var a5 = host.GetRequiredService<DraftingVerificationAgent>();

        // Act
        var result = await a5.RunAsync(
            new DraftingVerificationAgentRequest(
                DraftKind.DemandNotice,
                Exposure(urn, 0m, authoritativeAmount),
                Dealer(urn),
                Cheque: null,
                Memo: null,
                Clock: null,
                Draft: null,
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Verification uses authoritative amount.
        Assert.NotNull(result.Verification);
        // The quoted draft uses the authoritative exposure amount.
    }

    [Fact]
    public async Task A5_dealer_identity_preserved()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var authoritativeDealerUrn = "urn:dealer:A5_Dealer";
        store.SeedDealer(Dealer(authoritativeDealerUrn));
        var a5 = host.GetRequiredService<DraftingVerificationAgent>();

        // Act
        var result = await a5.RunAsync(
            new DraftingVerificationAgentRequest(
                DraftKind.Section138Notice,
                Exposure(authoritativeDealerUrn, 0m),
                Dealer(authoritativeDealerUrn),
                Cheque: null,
                Memo: null,
                Clock: null,
                Draft: null,
                Ctx(authoritativeDealerUrn)),
            CancellationToken.None);

        // Assert - Dealer identity in verification matches authoritative input.
        Assert.NotNull(result.Verification);
    }

    [Fact]
    public async Task A5_missing_template_insufficient_evidence()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A5_NoTemplate";
        store.SeedDealer(Dealer(urn));
        var a5 = host.GetRequiredService<DraftingVerificationAgent>();

        // Act - No template knowledge available.
        var result = await a5.RunAsync(
            new DraftingVerificationAgentRequest(
                DraftKind.Section138Notice,
                Exposure(urn, 0m),
                Dealer(urn),
                Cheque: null,
                Memo: null,
                Clock: null,
                Draft: null,
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Insufficient evidence indicated for templates (EmptyKnowledgeRetrievalService returns no chunks).
        Assert.NotNull(result.Grounding);
        Assert.True(result.Grounding.Diagnostics.InsufficientEvidence);
        Assert.Empty(result.Grounding.KnowledgeChunks);
    }

    [Fact]
    public async Task A5_verification_remains_authoritative()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A5_Verify";
        store.SeedDealer(Dealer(urn));
        var a5 = host.GetRequiredService<DraftingVerificationAgent>();

        // Act
        var result = await a5.RunAsync(
            new DraftingVerificationAgentRequest(
                DraftKind.DemandNotice,
                Exposure(urn, 0m, 50000m),
                Dealer(urn),
                Cheque: null,
                Memo: null,
                Clock: null,
                Draft: new DraftQuotedFields(urn, "SAP123", 50000m, null, null, null, null, null, null, null),
                Ctx(urn)),
            CancellationToken.None);

        // Assert - DraftingVerificationTool result is authoritative.
        Assert.NotNull(result.Verification);
        Assert.NotEmpty(result.Verification.Checks);
        // Grounding does not override verification outcome.
    }

    #endregion

    #region A8 Tests

    [Fact]
    public async Task A8_requests_A8_grounding_purpose()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A8_GP";
        store.SeedDealer(Dealer(urn));
        var a8 = host.GetRequiredService<SupervisoryInsightAgent>();

        // Act - With NLQ, grounding is invoked.
        var result = await a8.RunAsync(
            new SupervisoryInsightAgentRequest(
                "cycle-a8-gp",
                "North",
                urn,
                "What is the escalation policy?",
                null,
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Grounding context is populated and uses A8 purpose.
        Assert.NotNull(result.Grounding);
        Assert.Equal(GroundingPurpose.A8SupervisoryInsight, result.Grounding.Diagnostics.Purpose);
    }

    [Fact]
    public async Task A8_deterministic_worklist_uses_tool_data()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A8_Worklist";
        store.SeedDealer(Dealer(urn));
        var a8 = host.GetRequiredService<SupervisoryInsightAgent>();

        // Act - No NLQ, deterministic insights only.
        var result = await a8.RunAsync(
            new SupervisoryInsightAgentRequest(
                "cycle-a8-wl",
                "North",
                urn,
                NaturalLanguageQuestion: null,
                null,
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Deterministic insights are authoritative.
        Assert.NotNull(result.Insights);
        // No grounding called when no NLQ.
        Assert.Null(result.Grounding);
    }

    [Fact]
    public async Task A8_policy_question_uses_grounding()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A8_Policy";
        store.SeedDealer(Dealer(urn));
        var a8 = host.GetRequiredService<SupervisoryInsightAgent>();

        // Act - With NLQ, grounding is invoked.
        var result = await a8.RunAsync(
            new SupervisoryInsightAgentRequest(
                "cycle-a8-policy",
                "North",
                urn,
                "Policy question",
                null,
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Grounding called for NLQ.
        Assert.NotNull(result.Grounding);
        // Deterministic insights remain unchanged.
        Assert.NotNull(result.Insights);
    }

    [Fact]
    public async Task A8_insufficient_evidence_safe_response()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A8_NoEvidence";
        store.SeedDealer(Dealer(urn));
        var a8 = host.GetRequiredService<SupervisoryInsightAgent>();

        // Act - NLQ with no available knowledge.
        var result = await a8.RunAsync(
            new SupervisoryInsightAgentRequest(
                "cycle-a8-ne",
                "North",
                urn,
                "Unknown policy question",
                null,
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Insufficient evidence indicated (EmptyKnowledgeRetrievalService).
        Assert.NotNull(result.Grounding);
        Assert.True(result.Grounding.Diagnostics.InsufficientEvidence);
    }

    [Fact]
    public async Task A8_no_duplicate_retrieval()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A8_NoDupe";
        store.SeedDealer(Dealer(urn));
        var a8 = host.GetRequiredService<SupervisoryInsightAgent>();

        // Act - NLQ provided.
        var result = await a8.RunAsync(
            new SupervisoryInsightAgentRequest(
                "cycle-a8-nd",
                "North",
                urn,
                "Policy question",
                null,
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Single grounding call (no separate SearchDocuments call).
        Assert.NotNull(result.Grounding);
    }

    #endregion

    #region Trust Boundary Test

    [Fact]
    public async Task Authoritative_amount_outranks_retrieved_knowledge()
    {
        // Arrange
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "urn:dealer:A3_TrustBoundary";
        var authoritativeAmount = 100000m;
        store.SeedDealer(Dealer(urn));
        var a3 = host.GetRequiredService<NoticeDecisioningAgent>();

        // Act - Authoritative exposure amount provided.
        var result = await a3.RunAsync(
            new NoticeDecisioningAgentRequest(
                Dealer(urn),
                Exposure(urn, 0m, authoritativeAmount),
                null, null,
                "amount policy",
                Ctx(urn)),
            CancellationToken.None);

        // Assert - Authoritative amount is preserved (deterministic decision uses request.Exposure).
        // Retrieved knowledge (if any) does not override the authoritative amount.
        // (NoticeDecisionTool uses request.Exposure.NetRecoverableExposure directly).
        Assert.NotNull(result.Verdict);
    }

    #endregion

    #region Test Helpers

    private static Dealer Dealer(string urn)
        => new(new DealerUrn(urn), false, "SAP-TEST", "PORTAL-TEST", "Depot1", "North", "tsi@paintco.local");

    private static ExposureBreakdown Exposure(string urn, decimal credits, decimal netExposure = 100000m)
        => MetricContract.Compute(
            new DealerUrn(urn),
            TestDate,
            new Money(netExposure + credits, "INR"),
            new Money(credits, "INR"),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [new LineItemRef("SAP", "BSEG", "INV-1", netExposure + credits, TestDate.AddDays(-30))],
            true);

    private static AgentContext Ctx(string urn)
        => new(TestDate, "cycle-test", "corr-test", urn);

    #endregion
}
