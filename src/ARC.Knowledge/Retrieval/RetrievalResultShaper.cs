using ARC.Knowledge.Provenance;

namespace ARC.Knowledge.Retrieval;

public static class RetrievalResultShaper
{
    public static IReadOnlyList<EvidenceSource> Shape(
        IReadOnlyList<EvidenceSource> sources,
        int maxChunks,
        int maxSnippetCharacters)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shaped = new List<EvidenceSource>();
        foreach (var source in sources)
        {
            var key = $"{source.SourceDocumentId ?? source.Reference.DocumentId}|{source.Reference.PageOrSection}";
            if (!seen.Add(key))
                continue;

            var snippet = source.Snippet ?? "";
            if (snippet.Length > maxSnippetCharacters)
                snippet = snippet[..maxSnippetCharacters];

            shaped.Add(source with { Snippet = snippet });
            if (shaped.Count >= maxChunks)
                break;
        }

        return shaped;
    }
}
