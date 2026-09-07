using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Tests;

public sealed class A2ChatSafeContractTests
{
    private static ExposureBreakdown Exposure(decimal net, IReadOnlyList<LineItemRef>? lineage = null)
        => MetricContract.Compute(
            new DealerUrn("dealer:a2"),
            new DateOnly(2026, 3, 1),
            new Money(net),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            lineage ?? [],
            fullyReconciled: true);

    private static RiskAssessment Notice(decimal score)
        => new(RecoveryTier.Notice, score);

    [Fact]
    public void Score_remains_net_recoverable_exposure_and_formula_is_v1()
    {
        var exposure = Exposure(330_000m);
        var contract = A2ChatSafeAssembler.FromPrioritisation(
            exposure, Notice(330_000m), true, false, false, false);

        Assert.Equal(330_000m, contract.RecoverabilityScore);
        Assert.Equal(exposure.NetRecoverableExposure.Amount, contract.RecoverabilityScore);
        Assert.Equal("net_recoverable_exposure.v1", contract.ScoreFormula);
        Assert.Equal(MetricContract.Version, contract.ScoreFormula);
        Assert.Equal(RecoverabilityScoreFormula.Id, contract.ScoreFormula);
        Assert.Contains("not the assignment composite", contract.DeterministicExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Caller_cannot_supply_formula_tbc_or_provenance()
    {
        var facts = new A2ChatSafeFacts(
            "dealer:a2",
            12m,
            RecoveryTier.Notice,
            Exposure: Exposure(12m));

        var contract = A2ChatSafeAssembler.Assemble(facts);

        Assert.Equal("net_recoverable_exposure.v1", contract.ScoreFormula);
        Assert.DoesNotContain(contract.TbcIndicators, t => t.Status != "Tbc");
        Assert.Equal("NetRecoverableExposure", contract.Provenance.ScoreOrigin);
    }

    [Fact]
    public void Missing_payment_history_is_unavailable_and_formula_tbc_is_present()
    {
        var contract = A2ChatSafeAssembler.FromPrioritisation(
            Exposure(10_000m), Notice(10_000m), true, false, false, false);

        Assert.Equal(A2InputAvailability.Unavailable, Status(contract, "PaymentHistory"));
        Assert.Contains(contract.TbcIndicators, t => t.Id == "RecoverabilityFormula" && t.Status == "Tbc");
    }

    [Fact]
    public void Missing_graph_scoring_is_tbc_even_if_graph_context_exists()
    {
        var facts = new A2ChatSafeFacts(
            "dealer:a2",
            50_000m,
            RecoveryTier.Notice,
            Exposure: Exposure(50_000m),
            GraphFeaturesUsedInScore: false);

        var contract = A2ChatSafeAssembler.Assemble(facts);

        Assert.Equal(50_000m, contract.RecoverabilityScore);
        Assert.Equal(A2InputAvailability.Tbc, Status(contract, "GraphFeatures"));
        Assert.Contains(contract.TbcIndicators, t => t.Id == "GraphFeatureDefinition" && t.Status == "Tbc");
    }

    [Fact]
    public void Visit_cutoff_tbc_when_null()
    {
        var contract = A2ChatSafeAssembler.FromPrioritisation(
            Exposure(1m), Notice(1m), true, false, false, visitCutoffConfigured: false);

        var visit = contract.TbcIndicators.Single(t => t.Id == "VisitTierCutoff");
        Assert.Equal("Tbc", visit.Status);
        Assert.Contains("null", visit.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provenance_points_to_a1_lineage_when_present()
    {
        var lineage = new LineItemRef("ODOS", "odos_opening_data", "1|2026|07|002|109820|TRX|DOC", 330_000m, new DateOnly(2026, 7, 15));
        var contract = A2ChatSafeAssembler.FromPrioritisation(
            Exposure(330_000m, [lineage]), Notice(330_000m), true, false, false, false);

        Assert.True(contract.Provenance.Complete);
        Assert.Equal("Complete", contract.Provenance.Status);
        var row = Assert.Single(contract.Provenance.Lineage);
        Assert.Equal("ODOS", row.SourceSystem);
        Assert.Equal("odos_opening_data", row.SourceTable);
        Assert.Equal("1|2026|07|002|109820|TRX|DOC", row.SourceKey);
        Assert.Equal(330_000m, row.Amount);
    }

    [Fact]
    public void Missing_lineage_is_incomplete_and_not_fabricated()
    {
        var contract = A2ChatSafeAssembler.FromPrioritisation(
            Exposure(5_001m), Notice(5_001m), true, false, false, false);

        Assert.False(contract.Provenance.Complete);
        Assert.Equal("Incomplete", contract.Provenance.Status);
        Assert.Empty(contract.Provenance.Lineage);
        Assert.Contains("no source references were fabricated", contract.DeterministicExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tsi_remarks_presence_does_not_change_score()
    {
        var exposure = Exposure(100_000m);
        var without = A2ChatSafeAssembler.FromPrioritisation(exposure, Notice(100_000m), true, false, false, false);
        var with = A2ChatSafeAssembler.FromPrioritisation(
            exposure, Notice(100_000m), true, false, tsiRemarksPresent: true, false);

        Assert.Equal(without.RecoverabilityScore, with.RecoverabilityScore);
        Assert.Equal(100_000m, with.RecoverabilityScore);
        Assert.Equal(A2InputAvailability.Available, Status(with, "TsiRemarks"));
        Assert.Contains("narrated only", StatusDetail(with, "TsiRemarks"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Required_tbc_indicators_are_always_present()
    {
        var ids = A2ChatSafeAssembler.CentralTbcCatalog(false).Select(t => t.Id).ToArray();
        Assert.Contains("RecoverabilityFormula", ids);
        Assert.Contains("GraphFeatureDefinition", ids);
        Assert.Contains("TsiRemarkContribution", ids);
        Assert.Contains("VisitTierCutoff", ids);
        Assert.Contains("AgeingBucketMeaning", ids);
        Assert.Contains("EntryThresholdRelationship", ids);
        Assert.Contains("DealerAggregationRule", ids);
        Assert.Contains("DealerIdentityMapping", ids);
        Assert.Contains("TopDecilePopulation", ids);
        Assert.All(A2ChatSafeAssembler.CentralTbcCatalog(false), t => Assert.Equal("Tbc", t.Status));
    }

    [Fact]
    public void Worklist_projection_does_not_change_rank_or_score()
    {
        var contract = A2ChatSafeAssembler.FromWorklistEntry(
            "dealer:alpha", 50_000m, nameof(RecoveryTier.Notice), rank: 1, isTopDecile: true, false);

        Assert.Equal(1, contract.Rank);
        Assert.Equal(50_000m, contract.RecoverabilityScore);
        Assert.Equal("Notice", contract.Tier);
        Assert.True(contract.IsTopDecile);
        Assert.Equal("net_recoverable_exposure.v1", contract.ScoreFormula);
        Assert.Equal("Incomplete", contract.Provenance.Status);
    }

    [Fact]
    public void Completeness_covers_required_inputs()
    {
        var names = A2ChatSafeAssembler.FromPrioritisation(
            Exposure(1m), Notice(1m), true, true, false, false)
            .DataCompleteness.Select(c => c.Input).ToArray();

        Assert.Contains("NetRecoverableExposure", names);
        Assert.Contains("Outstanding", names);
        Assert.Contains("Ageing", names);
        Assert.Contains("PaymentHistory", names);
        Assert.Contains("GraphFeatures", names);
        Assert.Contains("TsiRemarks", names);
        Assert.Contains("VisitHistory", names);
        Assert.Contains("DealerIdentity", names);
        Assert.Contains("ChequeBounce", names);
        Assert.Contains("DemandNotice", names);
    }

    private static A2InputAvailability Status(A2ChatSafeContract contract, string input)
        => contract.DataCompleteness.Single(c => c.Input == input).Status;

    private static string? StatusDetail(A2ChatSafeContract contract, string input)
        => contract.DataCompleteness.Single(c => c.Input == input).Detail;
}
