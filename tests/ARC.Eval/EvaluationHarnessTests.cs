using ARC.Domain.Readiness;
using ARC.Eval.Golden;
using ARC.Eval.Metrics;

namespace ARC.Eval;

public sealed class EvaluationHarnessTests
{
    [Fact]
    public void Golden_run_states_DevelopmentOracle_authority()
    {
        var result = EvaluationRunBuilder.RunGoldenAcceptance(
            GoldenSetFactory.Create(),
            ARC.Domain.Rules.RuleConfiguration.SourceIllustrative());

        Assert.Equal(CorpusLabelAuthority.DevelopmentOracle, result.Metadata.LabelAuthority);
        Assert.Contains(result.Gates, g => g.GateId == "S138.Precision");
        Assert.Contains("DevelopmentOracle", result.Gates.First(g => g.GateId == "S138.Precision").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Citation_harness_without_scoring_definition_is_NOT_MEASURED()
    {
        var eval = CitationFaithfulnessHarness.Evaluate(
        [
            new CitationFaithfulnessRecord(
                "c1", "query", ["a"], ["a"], ["a"])
        ],
            scoringDefinition: null);

        Assert.Equal(EvaluationMeasurementStatus.NotMeasured, eval.Status);
    }

    [Fact]
    public void Citation_harness_with_scoring_definition_computes_deterministic_overlap()
    {
        var scoring = new StubScoring("overlap.v1", 0.95m);
        var eval = CitationFaithfulnessHarness.Evaluate(
        [
            new CitationFaithfulnessRecord("c1", "q", ["a"], ["a"], ["a"]),
            new CitationFaithfulnessRecord("c2", "q", ["b"], ["b"], ["b"]),
        ],
            scoring);

        Assert.Equal(EvaluationMeasurementStatus.Measured, eval.Status);
    }

    [Fact]
    public void Draft_amount_harness_without_labels_is_insufficient()
    {
        var eval = DraftAmountEvalHarness.Evaluate([], new ARC.Tools.Drafting.DraftingVerificationTool(Microsoft.Extensions.Logging.Abstractions.NullLogger<ARC.Tools.Drafting.DraftingVerificationTool>.Instance));
        Assert.Equal(EvaluationMeasurementStatus.InsufficientLabels, eval.Status);
    }

    private sealed class StubScoring(string id, decimal threshold) : ICitationScoringDefinition
    {
        public string DefinitionId => id;
        public decimal AcceptanceThreshold => threshold;
    }
}
