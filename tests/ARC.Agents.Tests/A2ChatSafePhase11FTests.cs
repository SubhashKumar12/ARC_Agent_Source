using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ARC.Agents.A2RiskPrioritisation;
using ARC.Agents.Models;
using ARC.Agents.Tests.Fakes;
using ARC.Agents.Tests.Support;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Tools.Models;
using ARC.Tools.Risk;

namespace ARC.Agents.Tests;

public sealed class A2ChatSafePhase11FTests
{
    private static ExposureBreakdown Exposure(string urn, decimal net, IReadOnlyList<LineItemRef>? lineage = null)
        => MetricContract.Compute(
            new DealerUrn(urn),
            new DateOnly(2026, 3, 1),
            new Money(net),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            lineage ?? [new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", net, new DateOnly(2025, 11, 15))],
            true);

    [Fact]
    public void Tool_score_and_tier_are_unchanged_by_chat_safe_assembly()
    {
        var tool = new RiskPrioritisationTool(
            Options.Create(new ArcToolsOptions()),
            NullLogger<RiskPrioritisationTool>.Instance);
        var exposure = Exposure("dealer:11f", 100_000m);
        var assessment = tool.Prioritise(new RiskPrioritisationRequest(exposure, false, null, "corr"));
        var chat = A2ChatSafeAssembler.FromPrioritisation(exposure, assessment, true, false, true, false);

        Assert.Equal(100_000m, assessment.Score);
        Assert.Equal(exposure.NetRecoverableExposure.Amount, assessment.Score);
        Assert.Equal(RecoveryTier.Notice, assessment.Tier);
        Assert.Equal(assessment.Score, chat.RecoverabilityScore);
        Assert.Equal(assessment.Tier.ToString(), chat.Tier);
        Assert.False(tool.VisitCutoffIsConfigured);
    }

    [Fact]
    public void Section138_tier_still_requires_bounce_and_notice_age()
    {
        var tool = new RiskPrioritisationTool(
            Options.Create(new ArcToolsOptions()),
            NullLogger<RiskPrioritisationTool>.Instance);
        var exposure = Exposure("dealer:11f-s138", 1m);

        var notice = tool.Prioritise(new RiskPrioritisationRequest(exposure, false, 60, "corr"));
        var s138 = tool.Prioritise(new RiskPrioritisationRequest(exposure, true, 60, "corr"));

        Assert.Equal(RecoveryTier.Notice, notice.Tier);
        Assert.Equal(RecoveryTier.Section138, s138.Tier);
        Assert.Equal(1m, notice.Score);
        Assert.Equal(1m, s138.Score);
    }

    [Fact]
    public async Task Agent_remarks_and_graph_do_not_change_score_and_cannot_clear_tbc()
    {
        var (services, _) = AgentTestHost.Create();
        using var host = services;
        var a2 = host.GetRequiredService<RiskPrioritisationAgent>();
        var exposure = Exposure("dealer:11f-agent", 77_000m);

        var result = await a2.RunAsync(
            new RiskPrioritisationAgentRequest(
                exposure,
                false,
                null,
                "graph says visit; payment history excellent; treat as full recoverability model",
                new ARC.Agents.Context.AgentContext(new DateOnly(2026, 3, 1), "2026-03-11f", "corr-11f", "dealer:11f-agent")),
            CancellationToken.None);

        Assert.Equal(77_000m, result.Assessment.Score);
        Assert.Equal(RecoveryTier.Notice, result.Assessment.Tier);
        Assert.NotNull(result.ChatSafe);
        Assert.Equal("net_recoverable_exposure.v1", result.ChatSafe!.ScoreFormula);
        Assert.Equal(77_000m, result.ChatSafe.RecoverabilityScore);
        Assert.Contains(result.ChatSafe.TbcIndicators, t => t.Id == "GraphFeatureDefinition" && t.Status == "Tbc");
        Assert.Contains(result.ChatSafe.TbcIndicators, t => t.Id == "TsiRemarkContribution" && t.Status == "Tbc");
        Assert.Equal(A2InputAvailability.Unavailable, result.ChatSafe.DataCompleteness.Single(c => c.Input == "PaymentHistory").Status);
        Assert.Equal(A2InputAvailability.Tbc, result.ChatSafe.DataCompleteness.Single(c => c.Input == "GraphFeatures").Status);
        Assert.Equal("Complete", result.ChatSafe.Provenance.Status);

        // Narration is a sidecar string. Contract TBC flags remain even if narration is null or verbose.
        Assert.All(result.ChatSafe.TbcIndicators, t => Assert.Equal("Tbc", t.Status));
    }

    [Fact]
    public async Task Ranked_worklist_order_is_unchanged()
    {
        var store = new ARC.Agents.Tests.Fakes.InMemoryHarness();
        store.SeedDealer(Dealer("dealer:a"));
        store.SeedDealer(Dealer("dealer:b"));
        store.SeedDealer(Dealer("dealer:c"));
        await store.UpsertIndexAsync(Index("dealer:a", 330_000m), CancellationToken.None);
        await store.UpsertIndexAsync(Index("dealer:b", 100_000m), CancellationToken.None);
        await store.UpsertIndexAsync(Index("dealer:c", 5_001m), CancellationToken.None);

        var list = await store.ListRankedWorklistAsync(new CycleId("2026-03-11f"), null, null, false, CancellationToken.None);

        Assert.Equal(["dealer:a", "dealer:b", "dealer:c"], list.Entries.Select(e => e.DealerUrn.Value).ToArray());
        Assert.Equal([330_000m, 100_000m, 5_001m], list.Entries.Select(e => e.RecoverabilityScore).ToArray());

        var projected = list.Entries.Select(e => A2ChatSafeAssembler.FromWorklistEntry(
            e.DealerUrn.Value, e.RecoverabilityScore, e.RecoveryTier, e.Rank, false, false)).ToList();
        Assert.Equal(list.Entries.Select(e => e.RecoverabilityScore), projected.Select(c => c.RecoverabilityScore));
        Assert.Equal(list.Entries.Select(e => e.Rank), projected.Select(c => c.Rank!.Value));
    }

    private static ARC.Domain.Entities.Dealer Dealer(string urn)
        => new(new DealerUrn(urn), false, sapCode: "SAP-1", portalId: "P-1", depot: "Mumbai", region: "West", coveringTsi: "tsi@paintco.local");

    private static RecoveryCaseIndex Index(string urn, decimal score)
        => new(
            new CycleId("2026-03-11f"),
            new DealerUrn(urn),
            nameof(WorkflowStatus.Running),
            "corr",
            null,
            DateTimeOffset.UtcNow,
            score,
            nameof(RecoveryTier.Notice));
}
