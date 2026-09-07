using ARC.Domain.Rules;
using ARC.Eval.Golden;
using ARC.Eval.Harness;
using ARC.Eval.Metrics;

namespace ARC.Eval.Metrics;

internal static class EvaluationRunBuilder
{
    public const string DefaultGoldenDatasetId = "arc.golden.label-oracle";
    public const string DefaultCorpusVersion = "2026.03.1";

    public static EvaluationRunResult RunGoldenAcceptance(
        IReadOnlyList<GoldenCase> cases,
        RuleConfiguration configuration,
        EvaluationThresholds? thresholds = null)
    {
        thresholds ??= new EvaluationThresholds();
        var report = EvalRunner.Run(cases, configuration);

        var metadata = new EvaluationDatasetMetadata(
            DefaultGoldenDatasetId,
            DefaultCorpusVersion,
            CorpusLabelAuthority.DevelopmentOracle,
            configuration.Version,
            DateTimeOffset.UtcNow,
            cases.Count,
            "LabelOracle priority rules — not independent human adjudication.");

        var metrics = new AcceptanceMetrics(
            report.CaseCount,
            report.Section138Classification,
            report.CitationFaithfulness,
            report.DraftAmount,
            report.WrongfulNoticeRate,
            report.LineageGapsOnIssue);

        var gates = new List<EvaluationGateResult>
        {
            Gate("S138.Precision", report.Section138Classification.Precision, thresholds.Section138PrecisionMin, metadata.LabelAuthority),
            Gate("S138.Recall", report.Section138Classification.Recall, thresholds.Section138RecallMin, metadata.LabelAuthority),
            Gate("WrongfulNoticeRate", report.WrongfulNoticeRate, thresholds.WrongfulNoticeRateMax, metadata.LabelAuthority, isMax: true),
            StatusGate("CitationFaithfulness", report.CitationFaithfulness.Status, report.CitationFaithfulness.Reason),
            StatusGate("DraftAmount", report.DraftAmount.Status, report.DraftAmount.Reason),
        };

        return new EvaluationRunResult(metadata, metrics, thresholds, gates);
    }

    private static EvaluationGateResult Gate(
        string id,
        decimal actual,
        decimal threshold,
        CorpusLabelAuthority authority,
        bool isMax = false)
    {
        var passed = isMax ? actual <= threshold : actual >= threshold;
        var status = authority == CorpusLabelAuthority.DevelopmentOracle
            ? EvaluationMeasurementStatus.Measured
            : EvaluationMeasurementStatus.Measured;

        var detail = authority == CorpusLabelAuthority.DevelopmentOracle
            ? "PASS_ON_CURRENT_CORPUS — DevelopmentOracle authority; not independent production acceptance."
            : "Measured on supplied corpus authority.";

        return new EvaluationGateResult(id, status, passed, actual, threshold, detail);
    }

    private static EvaluationGateResult StatusGate(string id, EvaluationMeasurementStatus status, string reason)
        => new(id, status, status == EvaluationMeasurementStatus.Measured, null, null, reason);
}
