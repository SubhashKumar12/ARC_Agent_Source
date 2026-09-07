namespace ARC.Eval.Retrieval;

/// <summary>Deterministic retrieval metrics for Stage 2. Network-free.</summary>
public static class RetrievalMetrics
{
    public static double RecallAtK(IReadOnlyCollection<string> relevant, IReadOnlyList<string> ranked, int k)
    {
        if (relevant.Count == 0)
            return 1.0;

        var top = ranked.Take(k).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hits = relevant.Count(r => top.Contains(r));
        return (double)hits / relevant.Count;
    }

    public static double HitRateAtK(IReadOnlyCollection<string> relevant, IReadOnlyList<string> ranked, int k)
    {
        if (relevant.Count == 0)
            return ranked.Take(k).Count() == 0 ? 1.0 : 0.0;

        var top = ranked.Take(k);
        return top.Any(id => relevant.Contains(id, StringComparer.OrdinalIgnoreCase)) ? 1.0 : 0.0;
    }

    public static double MeanReciprocalRank(IReadOnlyCollection<string> relevant, IReadOnlyList<string> ranked)
    {
        if (relevant.Count == 0)
            return ranked.Count == 0 ? 1.0 : 0.0;

        for (var i = 0; i < ranked.Count; i++)
        {
            if (relevant.Contains(ranked[i], StringComparer.OrdinalIgnoreCase))
                return 1.0 / (i + 1);
        }

        return 0.0;
    }

    public static double Average(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0.0 : list.Average();
    }

    public static bool IsCitationComplete(
        string? sourceDocumentId,
        string? pageOrSection,
        string? version,
        string? blobLocation)
        => !string.IsNullOrWhiteSpace(sourceDocumentId)
           && !string.IsNullOrWhiteSpace(pageOrSection)
           && !string.IsNullOrWhiteSpace(version)
           && !string.IsNullOrWhiteSpace(blobLocation);
}
