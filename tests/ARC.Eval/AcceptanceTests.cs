using Xunit.Abstractions;
using ARC.Eval.Golden;
using ARC.Eval.Harness;

namespace ARC.Eval;

/// <summary>
/// Golden-set acceptance against BRD §22. Labels are a BRD-priority oracle, not RuleEngine self-scores.
/// Retrieval faithfulness and Document Intelligence floors are not scored (no labelled corpus).
/// </summary>
public sealed class AcceptanceTests
{
    private readonly ITestOutputHelper _output;

    public AcceptanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Golden_set_meets_brd_acceptance_targets()
    {
        var set = GoldenSetFactory.Create();
        var report = EvalRunner.Run(set);
        _output.WriteLine(report.ToString());
        if (report.Failures.Count > 0)
        {
            _output.WriteLine("Failures:");
            foreach (var line in report.Failures.Take(25))
                _output.WriteLine("  " + line);
            if (report.Failures.Count > 25)
                _output.WriteLine($"  … {report.Failures.Count - 25} more");
        }

        Assert.True(set.Count >= 200, $"Golden set has {set.Count} cases; architecture target is ~200.");
        Assert.Equal(0, report.NoticeMismatches);
        Assert.True(report.WrongfulNoticeRate < 0.01m, $"Wrongful-notice rate {report.WrongfulNoticeRate:P2} (target < 1%).");
        Assert.Equal(0, report.LineageGapsOnIssue);
        Assert.Equal(0, report.Section138Mismatches);
        Assert.Equal(0, report.ClockMisses);
        Assert.Equal(0, report.ClockFalseAlarms);
        Assert.Equal(0, report.VoicePtpMismatches);
        Assert.Equal(0, report.VoicePtpConfirmedByTool);
        Assert.Equal(0, report.GovernanceFailures);
        Assert.True(report.Issued > 0, "Golden set must include Issue labels so wrongful-notice rate is defined.");
    }
}
