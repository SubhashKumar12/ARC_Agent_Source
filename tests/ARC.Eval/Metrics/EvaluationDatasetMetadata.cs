namespace ARC.Eval.Metrics;

/// <summary>Authority of labels used in an evaluation corpus. Never upgrade DevelopmentOracle to ProductionValidated without explicit sign-off.</summary>
public enum CorpusLabelAuthority
{
    DevelopmentOracle = 0,
    IndependentHumanLabel = 1,
    ProductionValidated = 2
}

public sealed record EvaluationThresholds(
    decimal Section138PrecisionMin = 0.95m,
    decimal Section138RecallMin = 0.98m,
    decimal CitationFaithfulnessMin = 0.95m,
    decimal WrongfulNoticeRateMax = 0.01m,
    decimal DraftAmountMatchRateMin = 1.00m);

public sealed record EvaluationDatasetMetadata(
    string DatasetId,
    string CorpusVersion,
    CorpusLabelAuthority LabelAuthority,
    string RuleConfigurationVersion,
    DateTimeOffset EvaluatedUtc,
    int CaseCount,
    string LabelProvenance);

public sealed record EvaluationRunResult(
    EvaluationDatasetMetadata Metadata,
    AcceptanceMetrics Metrics,
    EvaluationThresholds Thresholds,
    IReadOnlyList<EvaluationGateResult> Gates)
{
    public bool AllGatesPassed => Gates.All(g => g.Passed || g.Status == EvaluationMeasurementStatus.NotMeasured);
}

public sealed record EvaluationGateResult(
    string GateId,
    EvaluationMeasurementStatus Status,
    bool Passed,
    decimal? Actual,
    decimal? Threshold,
    string Detail);

public sealed record AcceptanceMetrics(
    int CaseCount,
    ClassificationMetrics Section138,
    CitationFaithfulnessEvaluation CitationFaithfulness,
    DraftAmountEvaluation DraftAmount,
    decimal WrongfulNoticeRate,
    int LineageGapsOnIssue);
