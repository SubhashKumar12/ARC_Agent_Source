using System.Text.RegularExpressions;

namespace ARC.Knowledge.Retrieval;

/// <summary>
/// Deterministic query routing. Identifiers and clause labels skip query embeddings.
/// </summary>
public static partial class RetrievalQueryRouter
{
    public static RetrievalQueryKind Route(string text, RetrievalMode mode, bool embeddingAvailable)
    {
        if (mode == RetrievalMode.Disabled)
            return RetrievalQueryKind.Lexical;

        if (mode == RetrievalMode.LexicalOnly || !embeddingAvailable)
            return RetrievalQueryKind.Lexical;

        if (LooksLikeIdentifierOrClause(text))
            return RetrievalQueryKind.Lexical;

        return RetrievalQueryKind.Vector;
    }

    public static bool LooksLikeIdentifierOrClause(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 64)
            return false;

        if (ClausePattern().IsMatch(trimmed))
            return true;

        if (trimmed.Contains(' '))
            return false;

        return IdentifierPattern().IsMatch(trimmed);
    }

    [GeneratedRegex(@"^(clause|section|para|paragraph)\s+[0-9]+([.][0-9]+)*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClausePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
