using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;

namespace ARC.Api.Mcp;

/// <summary>
/// Public MCP searchDocuments wording and additive citation mapping.
/// Current Hybrid configuration remains routed lexical-or-vector, not fused ranking.
/// </summary>
public static class McpSearchDocumentsContract
{
    public const string Description =
        "Routed lexical-or-vector retrieval over policy documents. Selects lexical CONTAINS or vector VectorDistance per query; does not fuse rankings. Respects region and authorized dealer scope.";

    public const string RetrievalBehavior = "routed lexical-or-vector";

    public static readonly string[] ForbiddenFusionClaims =
    [
        "hybrid fusion",
        "hybrid semantic search",
        "fused retrieval",
        "combined ranking"
    ];

    public static bool DescriptionClaimsFusion(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;

        var normalized = description.ToLowerInvariant();
        return ForbiddenFusionClaims.Any(claim =>
            normalized.Contains(claim, StringComparison.Ordinal));
    }

    public static McpSearchDocumentsResponse FromRetrieval(
        RetrievalResult result,
        string? appliedDealerUrn,
        string? appliedRegion)
    {
        var sources = result.Sources.Select(ToHit).ToList();
        return new McpSearchDocumentsResponse(
            sources.Count,
            sources,
            RetrievalBehavior,
            FormatRoute(result.RouteKind),
            NullIfEmpty(appliedDealerUrn),
            NullIfEmpty(appliedRegion));
    }

    public static McpSearchDocumentHit ToHit(EvidenceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new McpSearchDocumentHit(
            NullIfEmpty(source.Reference.DocumentId),
            NullIfEmpty(source.SourceDocumentId),
            NullIfEmpty(source.DocumentType),
            NullIfEmpty(source.Reference.SourceSystem),
            NullIfEmpty(source.Reference.Version),
            NullIfEmpty(source.Reference.PageOrSection),
            NullIfEmpty(source.Reference.BlobLocation),
            source.Reference.RetrievedUtc,
            source.Score,
            NullIfEmpty(source.DealerUrn),
            NullIfEmpty(source.RegionScope));
    }

    private static string? FormatRoute(RetrievalQueryKind? kind)
        => kind is null ? null : kind.Value.ToString();

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record McpSearchDocumentsResponse(
    int SourceCount,
    IReadOnlyList<McpSearchDocumentHit> Sources,
    string RetrievalBehavior,
    string? RetrievalRoute,
    string? AppliedDealerUrn,
    string? AppliedRegion);

public sealed record McpSearchDocumentHit(
    string? DocumentId,
    string? SourceDocumentId,
    string? DocumentType,
    string? SourceSystem,
    string? Version,
    string? PageOrSection,
    string? BlobLocation,
    DateTimeOffset? RetrievedUtc,
    double? Score,
    string? DealerUrn,
    string? RegionScope);
