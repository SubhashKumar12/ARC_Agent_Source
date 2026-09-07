using ARC.Knowledge.Configuration;

namespace ARC.Knowledge.Retrieval;

public static class RetrievalModeResolver
{
    public static RetrievalMode Resolve(ArcKnowledgeOptions options)
    {
        if (!options.RagEnabled)
            return RetrievalMode.Disabled;

        var parsed = Parse(options.RetrievalMode);
        if (parsed == RetrievalMode.Disabled)
            return RetrievalMode.Disabled;

        var vector = options.VectorSearchEnabled && parsed is RetrievalMode.VectorOnly or RetrievalMode.Hybrid;
        var lexical = options.LexicalSearchEnabled && parsed is RetrievalMode.LexicalOnly or RetrievalMode.Hybrid;

        if (vector && lexical)
            return RetrievalMode.Hybrid;
        if (vector)
            return RetrievalMode.VectorOnly;
        if (lexical)
            return RetrievalMode.LexicalOnly;
        return RetrievalMode.Disabled;
    }

    private static RetrievalMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return RetrievalMode.Hybrid;
        return Enum.TryParse<RetrievalMode>(value.Trim(), ignoreCase: true, out var mode)
            ? mode
            : RetrievalMode.Hybrid;
    }
}
