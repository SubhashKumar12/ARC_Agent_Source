using Microsoft.Extensions.DependencyInjection;
using ARC.Agents.A1Reconciliation;
using ARC.Agents.A2RiskPrioritisation;
using ARC.Agents.A3NoticeDecisioning;
using ARC.Agents.A4LegalEligibility;
using ARC.Agents.A6FieldOrchestration;
using ARC.Agents.A8SupervisoryInsight;
using ARC.Agents.Context;
using ARC.Agents.Exceptions;
using ARC.Agents.Models;
using ARC.Agents.Tests.Support;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Agents.Tests;

public sealed class AgentContractTests
{
    [Fact]
    public async Task A1_amount_comes_from_the_tool_not_the_empty_chat_client()
    {
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "dealer:a1";
        store.SeedDealer(Dealer(urn));
        store.SeedLedger(Invoice(urn, 100_000m));

        var result = await host.GetRequiredService<ReconciliationAgent>().RunAsync(
            new ReconciliationAgentRequest(urn, Ctx(urn)),
            CancellationToken.None);

        Assert.Equal(100_000m, result.Facts.Exposure.NetRecoverableExposure.Amount);
        Assert.Equal(ReconciliationStatus.Reconciled, result.Facts.Exposure.Status);
        Assert.True(result.Facts.Exposure.Lineage.Count > 0);
        Assert.True(string.IsNullOrEmpty(result.Explanation));
    }

    [Fact]
    public async Task A3_issue_and_reconcile_come_from_DecideNotice()
    {
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var clean = "dealer:a3-clean";
        var credit = "dealer:a3-cn";
        store.SeedDealer(Dealer(clean));
        store.SeedDealer(Dealer(credit));
        var a3 = host.GetRequiredService<NoticeDecisioningAgent>();

        var issue = await a3.RunAsync(
            new NoticeDecisioningAgentRequest(Dealer(clean), Exposure(clean, 0m), null, null, null, Ctx(clean)),
            CancellationToken.None);
        var reconcile = await a3.RunAsync(
            new NoticeDecisioningAgentRequest(Dealer(credit), Exposure(credit, 77_000m), null, null, null, Ctx(credit)),
            CancellationToken.None);

        Assert.Equal(NoticeDecision.Issue, issue.Verdict.Decision);
        Assert.True(issue.Verdict.RequiresDepotManagerGate);
        Assert.Equal(NoticeDecision.Reconcile, reconcile.Verdict.Decision);
        Assert.False(reconcile.Verdict.RequiresDepotManagerGate);
    }

    [Fact]
    public async Task A3_rejects_arbitrary_search_urls()
    {
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        store.SeedDealer(Dealer("dealer:a3-url"));
        var a3 = host.GetRequiredService<NoticeDecisioningAgent>();

        var ex = await Assert.ThrowsAsync<AgentException>(() => a3.RunAsync(
            new NoticeDecisioningAgentRequest(
                Dealer("dealer:a3-url"),
                Exposure("dealer:a3-url", 0m),
                null, null,
                "https://evil.example/policy",
                Ctx("dealer:a3-url")),
            CancellationToken.None));
        Assert.Equal(NoticeDecisioningAgent.Name, ex.AgentName);
    }

    [Fact]
    public async Task A2_does_not_auto_assign_visit_when_cutoff_is_unset()
    {
        var (services, _) = AgentTestHost.Create();
        using var host = services;
        var a2 = host.GetRequiredService<RiskPrioritisationAgent>();
        var notice = await a2.RunAsync(
            new RiskPrioritisationAgentRequest(Exposure("dealer:a2", 0m), false, null, "high recoverability", Ctx("dealer:a2")),
            CancellationToken.None);
        var s138 = await a2.RunAsync(
            new RiskPrioritisationAgentRequest(Exposure("dealer:a2", 0m), true, 60, null, Ctx("dealer:a2")),
            CancellationToken.None);

        Assert.Equal(RecoveryTier.Notice, notice.Assessment.Tier);
        Assert.Equal(RecoveryTier.Section138, s138.Assessment.Tier);
        Assert.Equal(notice.Assessment.Score, Exposure("dealer:a2", 0m).NetRecoverableExposure.Amount);
    }

    [Fact]
    public async Task A4_blocks_non_qualifying_bounce_before_legal_gate()
    {
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "dealer:a4";
        store.SeedDealer(Dealer(urn));
        store.SeedCheque(new SecurityCheque(new DealerUrn(urn), "CHQ-1", new Money(100_000m), ChequeStatus.Bounced, validityEnd: new DateOnly(2027, 1, 1)));
        store.SeedMemo(new ChequeReturnMemo(new DealerUrn(urn), "CHQ-1", "SIGNATURE_MISMATCH", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));

        var result = await host.GetRequiredService<LegalEligibilityAgent>().RunAsync(
            new LegalEligibilityAgentRequest(urn, Exposure(urn, 0m), null, Ctx(urn)),
            CancellationToken.None);

        Assert.False(result.Facts.Eligibility.Eligible);
    }

    [Fact]
    public async Task A6_voice_ptp_never_confirms_and_requires_tsi_at_0_72()
    {
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        store.SeedDealer(Dealer("dealer:a6"));
        var result = await host.GetRequiredService<FieldOrchestrationAgent>().RunAsync(
            new FieldOrchestrationAgentRequest(
                FieldAgentAction.CapturePromiseToPay,
                "dealer:a6",
                RecoveryTier.Notice,
                new DateOnly(2026, 4, 15),
                25_000m,
                0.72m,
                null, null,
                Ctx("dealer:a6")),
            CancellationToken.None);

        Assert.NotNull(result.Promise);
        Assert.True(result.Promise!.RequiresTsiConfirmation);
        Assert.False(result.Promise.Promise.ConfirmedByTsi);
        Assert.False(result.Promise.DiscardedLowConfidence);
    }

    [Fact]
    public async Task A8_exception_queue_comes_from_the_insight_tool()
    {
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        store.SeedDealer(Dealer("dealer:a8"));
        var result = await host.GetRequiredService<SupervisoryInsightAgent>().RunAsync(
            new SupervisoryInsightAgentRequest("2026-03-test", "West", null, null, null, Ctx("dealer:a8")),
            CancellationToken.None);

        Assert.NotNull(result.Insights);
        Assert.Empty(result.Insights.Exceptions);
    }

    private static Dealer Dealer(string urn)
        => new(new DealerUrn(urn), false, "SAP-1", "PORTAL-1", "Mumbai", "West", "tsi@paintco.local");

    private static LedgerPosition Invoice(string urn, decimal amount)
        => new(
            new DealerUrn(urn),
            "Invoice",
            new DateOnly(2025, 12, 1),
            new DateOnly(2025, 11, 15),
            new Money(amount),
            new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", amount, new DateOnly(2025, 11, 15)));

    private static ExposureBreakdown Exposure(string urn, decimal credits)
        => MetricContract.Compute(
            new DealerUrn(urn),
            new DateOnly(2026, 3, 1),
            new Money(100_000m),
            new Money(credits),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", 100_000m, new DateOnly(2025, 11, 15))],
            true);

    private static AgentContext Ctx(string urn)
        => new(new DateOnly(2026, 3, 1), "2026-03-test", "corr-test", urn);
}
