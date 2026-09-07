namespace ARC.Eval.Metrics;

/// <summary>
/// Compares deterministic expected draft amount to actual only when authoritative expected amount exists.
/// Never uses LLM to determine expected amount.
/// </summary>
public sealed record DraftAmountEvaluation(
    EvaluationMeasurementStatus Status,
    int CasesWithAuthoritativeExpectedAmount,
    int Compared,
    int Matches,
    int Mismatches,
    string Reason)
{
    public decimal? MatchRate =>
        Compared == 0 ? null : (decimal)Matches / Compared;

    public static DraftAmountEvaluation InsufficientLabels(int noticeCases) => new(
        EvaluationMeasurementStatus.InsufficientLabels,
        CasesWithAuthoritativeExpectedAmount: 0,
        Compared: 0,
        Matches: 0,
        Mismatches: 0,
        Reason: $"Golden set has {noticeCases} notice cases but none carry an authoritative expected draft amount field.");

    public override string ToString()
    {
        var rate = MatchRate is { } r ? $" matchRate={r:P2}" : "";
        return $"DraftAmount: {Status} compared={Compared} matches={Matches} mismatches={Mismatches}{rate} — {Reason}";
    }
}
