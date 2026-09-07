namespace ARC.Eval.Metrics;

/// <summary>Approved external citation evaluation record. Labels must be supplied — never LLM-generated.</summary>
public sealed record CitationFaithfulnessRecord(
    string CaseId,
    string Query,
    IReadOnlyList<string> RetrievedEvidenceIds,
    IReadOnlyList<string> ExpectedEvidenceIds,
    IReadOnlyList<string> AnswerCitations);

/// <summary>
/// Deterministic set-overlap metrics. Does not claim assignment acceptance until DEC-M05 scoring is approved.
/// </summary>
public sealed record CitationOverlapMetrics(
    int TruePositiveRetrieval,
    int FalsePositiveRetrieval,
    int FalseNegativeRetrieval,
    decimal? RetrievalPrecision,
    decimal? RetrievalRecall,
    int TruePositiveCitation,
    int FalsePositiveCitation,
    decimal? CitationPrecision)
{
    public override string ToString()
        => $"Retrieval PR={RetrievalPrecision:P2} RC={RetrievalRecall:P2}  CitationP={CitationPrecision:P2}";
}

public interface ICitationScoringDefinition
{
    string DefinitionId { get; }
    decimal AcceptanceThreshold { get; }
}

public static class CitationFaithfulnessHarness
{
    public static CitationFaithfulnessEvaluation Evaluate(
        IReadOnlyList<CitationFaithfulnessRecord> records,
        ICitationScoringDefinition? scoringDefinition)
    {
        if (records.Count == 0)
        {
            return new CitationFaithfulnessEvaluation(
                EvaluationMeasurementStatus.InsufficientLabels,
                "No citation faithfulness records supplied.");
        }

        if (scoringDefinition is null)
        {
            return CitationFaithfulnessEvaluation.NotMeasured();
        }

        var metrics = records.Select(ScoreRecord).ToList();
        var avgRetrievalPrecision = Average(metrics.Select(m => m.RetrievalPrecision));
        var passed = avgRetrievalPrecision >= scoringDefinition.AcceptanceThreshold;

        return new CitationFaithfulnessEvaluation(
            passed ? EvaluationMeasurementStatus.Measured : EvaluationMeasurementStatus.Tbc,
            $"ScoringDefinition={scoringDefinition.DefinitionId} cases={records.Count} " +
            $"avgRetrievalPrecision={avgRetrievalPrecision:P2} threshold={scoringDefinition.AcceptanceThreshold:P2}");
    }

    public static CitationOverlapMetrics ScoreRecord(CitationFaithfulnessRecord record)
    {
        var retrieved = new HashSet<string>(record.RetrievedEvidenceIds, StringComparer.OrdinalIgnoreCase);
        var expected = new HashSet<string>(record.ExpectedEvidenceIds, StringComparer.OrdinalIgnoreCase);
        var citations = new HashSet<string>(record.AnswerCitations, StringComparer.OrdinalIgnoreCase);

        var tpR = retrieved.Count(id => expected.Contains(id));
        var fpR = retrieved.Count - tpR;
        var fnR = expected.Count(id => !retrieved.Contains(id));

        var tpC = citations.Count(id => expected.Contains(id));
        var fpC = citations.Count - tpC;

        return new CitationOverlapMetrics(
            tpR, fpR, fnR,
            Precision(tpR, fpR),
            Recall(tpR, fnR),
            tpC, fpC,
            Precision(tpC, fpC));
    }

    private static decimal? Precision(int tp, int fp)
        => tp + fp == 0 ? null : (decimal)tp / (tp + fp);

    private static decimal? Recall(int tp, int fn)
        => tp + fn == 0 ? null : (decimal)tp / (tp + fn);

    private static decimal Average(IEnumerable<decimal?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count == 0 ? 0m : list.Average();
    }
}
