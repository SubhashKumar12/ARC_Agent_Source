namespace ARC.Eval.Metrics;

/// <summary>Binary classification counts and derived precision/recall.</summary>
public sealed record ClassificationMetrics(
    int TruePositives,
    int FalsePositives,
    int TrueNegatives,
    int FalseNegatives)
{
    public int Total => TruePositives + FalsePositives + TrueNegatives + FalseNegatives;

    public decimal Precision =>
        TruePositives + FalsePositives == 0 ? 0m : (decimal)TruePositives / (TruePositives + FalsePositives);

    public decimal Recall =>
        TruePositives + FalseNegatives == 0 ? 0m : (decimal)TruePositives / (TruePositives + FalseNegatives);

    public override string ToString()
        => $"TP={TruePositives} FP={FalsePositives} TN={TrueNegatives} FN={FalseNegatives} " +
           $"Precision={Precision:P2} Recall={Recall:P2} (n={Total})";
}
