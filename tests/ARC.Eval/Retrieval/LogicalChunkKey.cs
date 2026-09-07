namespace ARC.Eval.Retrieval;

/// <summary>Logical chunk identity for relevance labels: source|version|pageOrSection.</summary>
public static class LogicalChunkKey
{
    public static string Create(string sourceDocumentId, string version, string pageOrSection)
        => $"{sourceDocumentId.Trim()}|{version.Trim()}|{pageOrSection.Trim()}";
}
