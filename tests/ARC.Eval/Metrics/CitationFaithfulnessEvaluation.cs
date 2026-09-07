namespace ARC.Eval.Metrics;

/// <summary>
/// Citation faithfulness evaluation contract. No scoring algorithm is implemented —
/// awaiting Management-approved labelled retrieval corpus and metric definition.
/// </summary>
public sealed record CitationFaithfulnessEvaluation(
    EvaluationMeasurementStatus Status,
    string Reason)
{
    public static CitationFaithfulnessEvaluation NotMeasured() => new(
        EvaluationMeasurementStatus.NotMeasured,
        "No approved labelled citation corpus or faithfulness scoring algorithm. " +
        "AcceptanceTests explicitly excludes this metric.");

    public override string ToString() => $"CitationFaithfulness: {Status} — {Reason}";
}

/// <summary>Contract for future citation faithfulness evaluation when corpus exists.</summary>
public interface ICitationFaithfulnessEvaluator
{
    CitationFaithfulnessEvaluation Evaluate();
}

internal sealed class NotConfiguredCitationFaithfulnessEvaluator : ICitationFaithfulnessEvaluator
{
    public CitationFaithfulnessEvaluation Evaluate() => CitationFaithfulnessEvaluation.NotMeasured();
}
