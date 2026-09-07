using ARC.Eval.Golden;
using ARC.Eval.Harness;
using ARC.Eval.Metrics;
using Xunit.Abstractions;

namespace ARC.Eval;

/// <summary>
/// Section 138 precision/recall scaffolding. Labels come from LabelOracle (same priority rules as harness).
/// This is not an independent human-labelled production corpus — see coverage note in test output.
/// </summary>
public sealed class Section138MetricsTests
{
    private readonly ITestOutputHelper _output;

    public Section138MetricsTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Section138_precision_recall_computed_from_explicit_eligibility_labels()
    {
        var set = GoldenSetFactory.Create();
        var report = EvalRunner.Run(set);
        var metrics = report.Section138Classification;

        _output.WriteLine(metrics.ToString());
        _output.WriteLine(
            "Coverage note: labels are LabelOracle-derived (BRD priority), not independent human adjudication. " +
            "Assignment-quality PR acceptance requires Management/Trainer labelled corpus sign-off.");

        Assert.True(set.Count(c => c.Kind == GoldenKind.Section138) >= 50,
            "Insufficient S138 labelled cases for meaningful PR reporting.");
        Assert.All(set.Where(c => c.Kind == GoldenKind.Section138), c => Assert.NotNull(c.ExpectedEligible));
        Assert.Equal(0, report.Section138Mismatches);
        Assert.Equal(metrics.Total, report.Section138Cases);
        Assert.Equal(1.0m, metrics.Precision);
        Assert.Equal(1.0m, metrics.Recall);
    }

    [Fact]
    public void Citation_faithfulness_is_not_measured_pending_corpus()
    {
        var report = EvalRunner.Run(GoldenSetFactory.Create());
        Assert.Equal(EvaluationMeasurementStatus.NotMeasured, report.CitationFaithfulness.Status);
        _output.WriteLine(report.CitationFaithfulness.ToString());
    }

    [Fact]
    public void Draft_amount_evaluation_reports_insufficient_labels()
    {
        var report = EvalRunner.Run(GoldenSetFactory.Create());
        Assert.Equal(EvaluationMeasurementStatus.InsufficientLabels, report.DraftAmount.Status);
        Assert.Equal(0, report.DraftAmount.Compared);
        _output.WriteLine(report.DraftAmount.ToString());
    }
}
