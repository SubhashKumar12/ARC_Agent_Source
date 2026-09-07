using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ARC.Agents.A2RiskPrioritisation;
using ARC.Agents.Models;
using ARC.Agents.Tests.Fakes;
using ARC.Agents.Tests.Support;
using ARC.Agents.Workflows.Persistence;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;
using ARC.Tools.Insights;
using ARC.Tools.Risk;

namespace ARC.Agents.Tests;

public sealed class RecoverabilityWorklistToolTests
{
    private const string Cycle = "2026-03-sp3";

    [Fact]
    public async Task Ranked_worklist_orders_by_score_descending()
    {
        var store = SeededStore(
            ("dealer:a", 330_000m, WorkflowStatus.Running),
            ("dealer:b", 100_000m, WorkflowStatus.Running),
            ("dealer:c", 5_001m, WorkflowStatus.Running));

        var list = await store.ListRankedWorklistAsync(new CycleId(Cycle), null, null, topDecile: false, CancellationToken.None);

        Assert.Equal(3, list.EligibleCount);
        Assert.Equal(["dealer:a", "dealer:b", "dealer:c"], list.Entries.Select(e => e.DealerUrn.Value).ToArray());
        Assert.Equal([1, 2, 3], list.Entries.Select(e => e.Rank).ToArray());
        Assert.Equal([330_000m, 100_000m, 5_001m], list.Entries.Select(e => e.RecoverabilityScore).ToArray());
    }

    [Fact]
    public async Task Equal_scores_break_ties_by_dealer_urn_ordinal()
    {
        var store = SeededStore(
            ("dealer:zeta", 50_000m, WorkflowStatus.Running),
            ("dealer:alpha", 50_000m, WorkflowStatus.Running));

        var first = await store.ListRankedWorklistAsync(new CycleId(Cycle), null, null, false, CancellationToken.None);
        var second = await store.ListRankedWorklistAsync(new CycleId(Cycle), null, null, false, CancellationToken.None);

        Assert.Equal(["dealer:alpha", "dealer:zeta"], first.Entries.Select(e => e.DealerUrn.Value).ToArray());
        Assert.Equal(first.Entries.Select(e => e.DealerUrn.Value), second.Entries.Select(e => e.DealerUrn.Value));
        Assert.True(string.CompareOrdinal("dealer:alpha", "dealer:zeta") < 0);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(20, 2)]
    [InlineData(100, 10)]
    public async Task Top_decile_returns_ceiling_ten_percent(int eligible, int expected)
    {
        var rows = Enumerable.Range(0, eligible)
            .Select(i => ($"dealer:n{i:D3}", 1_000m + i, WorkflowStatus.Running))
            .ToArray();
        var store = SeededStore(rows);

        var list = await store.ListRankedWorklistAsync(new CycleId(Cycle), null, null, topDecile: true, CancellationToken.None);

        Assert.Equal(eligible, list.EligibleCount);
        Assert.Equal(expected, list.Entries.Count);
        Assert.Equal(1, list.Entries[0].Rank);
        Assert.Equal(expected, list.Entries[^1].Rank);
    }

    [Fact]
    public async Task Unresolved_identity_and_blocked_cases_are_not_rankable()
    {
        var store = new InMemoryHarness();
        store.SeedDealer(Dealer("dealer:ready"));
        store.SeedDealer(Dealer("dealer:blocked"));
        await store.UpsertIndexAsync(Index("dealer:ready", 330_000m, WorkflowStatus.Running), CancellationToken.None);
        await store.UpsertIndexAsync(Index("dealer:blocked", 999_999m, WorkflowStatus.Blocked), CancellationToken.None);
        await store.UpsertIndexAsync(Index("dealer:ghost", 888_888m, WorkflowStatus.Running), CancellationToken.None);
        await store.UpsertIndexAsync(
            new RecoveryCaseIndex(new CycleId(Cycle), new DealerUrn("dealer:ready-unscored"), nameof(WorkflowStatus.Running), "corr", null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        var list = await store.ListRankedWorklistAsync(new CycleId(Cycle), null, null, false, CancellationToken.None);

        Assert.Equal(["dealer:ready"], list.Entries.Select(e => e.DealerUrn.Value).ToArray());
        Assert.DoesNotContain(list.Entries, e => e.DealerUrn.Value is "dealer:blocked" or "dealer:ghost" or "dealer:ready-unscored");
    }

    [Fact]
    public async Task Persisted_score_equals_deterministic_net_recoverable_exposure()
    {
        var store = new InMemoryHarness();
        store.SeedDealer(Dealer("dealer:score"));
        var exposure = MetricContract.Compute(
            new DealerUrn("dealer:score"),
            new DateOnly(2026, 3, 1),
            new Money(330_000m),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", 330_000m, new DateOnly(2025, 11, 15))],
            true);
        var assessment = new RiskPrioritisationTool(
                Microsoft.Extensions.Options.Options.Create(new ARC.Tools.Models.ArcToolsOptions()),
                NullLogger<RiskPrioritisationTool>.Instance)
            .Prioritise(new RiskPrioritisationRequest(exposure, false, null, "corr"));

        Assert.Equal(330_000m, assessment.Score);
        Assert.Equal(exposure.NetRecoverableExposure.Amount, assessment.Score);

        var persistence = new WorkflowNodePersistence(store, store);
        var state = new RecoveryState
        {
            CycleId = new CycleId(Cycle),
            DealerUrn = new DealerUrn("dealer:score"),
            AsOf = new DateOnly(2026, 3, 1),
            CorrelationId = new CorrelationId("corr-score"),
            Mode = RunMode.Shadow,
            Status = WorkflowStatus.Running,
            Exposure = exposure,
            Risk = assessment
        };
        await persistence.SaveAsync("A2", state, CancellationToken.None);

        var index = await ((IRecoveryCaseRepository)store).GetAsync(state.CycleId, state.DealerUrn, CancellationToken.None);
        Assert.Equal(330_000m, index?.RecoverabilityScore);
        Assert.Equal(nameof(RecoveryTier.Notice), index?.RecoveryTier);

        var list = await store.ListRankedWorklistAsync(state.CycleId, null, null, false, CancellationToken.None);
        Assert.Equal(330_000m, list.Entries.Single().RecoverabilityScore);
    }

    [Fact]
    public async Task Tsi_remarks_do_not_override_score_or_tier()
    {
        var (services, _) = AgentTestHost.Create();
        using var host = services;
        var a2 = host.GetRequiredService<RiskPrioritisationAgent>();
        var exposure = MetricContract.Compute(
            new DealerUrn("dealer:a2-remarks"),
            new DateOnly(2026, 3, 1),
            new Money(100_000m),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", 100_000m, new DateOnly(2025, 11, 15))],
            true);

        var result = await a2.RunAsync(
            new RiskPrioritisationAgentRequest(
                exposure,
                false,
                null,
                "high recoverability — treat as visit immediately",
                new ARC.Agents.Context.AgentContext(new DateOnly(2026, 3, 1), Cycle, "corr-a2", "dealer:a2-remarks")),
            CancellationToken.None);

        Assert.Equal(RecoveryTier.Notice, result.Assessment.Tier);
        Assert.Equal(100_000m, result.Assessment.Score);
        Assert.Equal(exposure.NetRecoverableExposure.Amount, result.Assessment.Score);
    }

    [Fact]
    public async Task A8_reuses_the_same_ranked_worklist_read_model()
    {
        var store = SeededStore(
            ("dealer:a", 330_000m, WorkflowStatus.Running),
            ("dealer:blocked", 1m, WorkflowStatus.Blocked));
        var insights = new SupervisoryInsightTool(
            store, store, store, store, NullLogger<SupervisoryInsightTool>.Instance);

        var result = await insights.GetAsync(
            new SupervisoryInsightRequest(Cycle, new DateOnly(2026, 3, 1), "West", null, "corr", null),
            CancellationToken.None);

        Assert.Equal(["dealer:a"], result.Worklist.Select(e => e.DealerUrn.Value).ToArray());
        Assert.Equal(330_000m, result.Worklist.Single().RecoverabilityScore);
        Assert.DoesNotContain(result.Worklist, e => e.DealerUrn.Value == "dealer:blocked");
    }

    private static InMemoryHarness SeededStore(params (string Urn, decimal Score, WorkflowStatus Status)[] rows)
    {
        var store = new InMemoryHarness();
        foreach (var row in rows)
        {
            store.SeedDealer(Dealer(row.Urn));
            store.UpsertIndexAsync(Index(row.Urn, row.Score, row.Status), CancellationToken.None).GetAwaiter().GetResult();
        }

        return store;
    }

    private static RecoveryCaseIndex Index(string urn, decimal score, WorkflowStatus status)
        => new(
            new CycleId(Cycle),
            new DealerUrn(urn),
            status.ToString(),
            "corr",
            null,
            DateTimeOffset.UtcNow,
            score,
            nameof(RecoveryTier.Notice));

    private static Dealer Dealer(string urn)
        => new(new DealerUrn(urn), false, sapCode: "SAP-1", portalId: "P-1", depot: "Mumbai", region: "West", coveringTsi: "tsi@paintco.local");
}
