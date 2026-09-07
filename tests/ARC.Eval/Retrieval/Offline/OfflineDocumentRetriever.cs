using ARC.Knowledge.Configuration;
using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;
using ARC.Knowledge.Vector;

namespace ARC.Eval.Retrieval.Offline;

/// <summary>
/// In-memory retriever mirroring ARC metadata filters + lexical CONTAINS + fake vector ranking.
/// FRAMEWORK VALIDATION ONLY — not Cosmos DiskANN / Azure embedding quality.
/// </summary>
public sealed class OfflineDocumentRetriever : IDocumentRetriever
{
    private readonly IReadOnlyList<IndexedDocument> _documents;

    public OfflineDocumentRetriever(IReadOnlyList<IndexedDocument> documents)
    {
        _documents = documents;
    }

    public int QueryCount { get; private set; }

    public void ResetCounters() => QueryCount = 0;

    public Task<IReadOnlyList<EvidenceSource>> RetrieveAsync(
        DocumentRetrievalRequest request,
        CancellationToken cancellationToken)
    {
        QueryCount++;
        var filtered = _documents.Where(d => MatchesFilter(d, request.Filter)).ToList();

        IEnumerable<IndexedDocument> ranked;
        if (request.Kind == RetrievalQueryKind.Vector && request.QueryEmbedding is { Length: > 0 } queryEmbedding)
        {
            ranked = filtered
                .Where(d => d.embedding is { Length: > 0 })
                .Select(d =>
                {
                    d.similarityScore = BagOfWordsEmbeddingProvider.CosineDistance(queryEmbedding, d.embedding!);
                    return d;
                })
                .OrderBy(d => d.similarityScore)
                .ThenBy(d => d.id, StringComparer.Ordinal);
        }
        else
        {
            var text = request.Text ?? "";
            ranked = filtered
                .Where(d => LexicalMatch(d, text))
                .OrderBy(d => d.id, StringComparer.Ordinal); // stable; no lexical score (matches Cosmos)
        }

        var results = ranked
            .Take(request.TopK)
            .Select(ToSource)
            .ToList();

        return Task.FromResult<IReadOnlyList<EvidenceSource>>(results);
    }

    private static bool MatchesFilter(IndexedDocument d, DocumentRetrievalFilter filter)
    {
        if (!string.Equals(d.status, ArcKnowledgeOptions.ActiveStatus, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(d.version, filter.Version, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(filter.DocumentCategory)
            && !string.Equals(d.documentCategory, filter.DocumentCategory, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(filter.DocumentType)
            && !string.Equals(d.documentType, filter.DocumentType, StringComparison.OrdinalIgnoreCase))
            return false;

        // Dealer: global (null/empty) visible to all; scoped requires match.
        if (!string.IsNullOrWhiteSpace(d.dealerUrn))
        {
            if (string.IsNullOrWhiteSpace(filter.DealerUrn)
                || !string.Equals(d.dealerUrn, filter.DealerUrn, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Region: empty/GLOBAL in scope, or actor region contained.
        var scope = d.regionScope ?? Array.Empty<string>();
        if (scope.Length > 0)
        {
            var hasGlobal = scope.Any(r => string.Equals(r, ArcKnowledgeOptions.GlobalRegionToken, StringComparison.OrdinalIgnoreCase));
            if (!hasGlobal)
            {
                if (string.IsNullOrWhiteSpace(filter.Region)
                    || !scope.Any(r => string.Equals(r, filter.Region, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
        }

        return true;
    }

    private static bool LexicalMatch(IndexedDocument d, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        return Contains(d.content, text)
               || Contains(d.title, text)
               || Contains(d.pageOrSection, text);
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack)
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static EvidenceSource ToSource(IndexedDocument doc)
    {
        return new EvidenceSource(
            new SourceReference(
                doc.id,
                doc.blobLocation,
                null,
                doc.version,
                doc.pageOrSection,
                "offline-synthetic",
                DateTimeOffset.UtcNow),
            string.IsNullOrWhiteSpace(doc.title) ? doc.id : doc.title,
            doc.content ?? "",
            doc.similarityScore,
            doc.status,
            RegionScope: doc.regionScope is { Length: > 0 } ? string.Join(',', doc.regionScope) : null,
            DealerUrn: doc.dealerUrn,
            SourceDocumentId: doc.sourceDocumentId,
            DocumentType: doc.documentType);
    }
}
