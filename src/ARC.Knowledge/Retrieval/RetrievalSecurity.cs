using ARC.Knowledge.Configuration;

namespace ARC.Knowledge.Retrieval;

/// <summary>
/// Mandatory retrieval authorization. Status/version/region/dealer are not LLM-controlled.
/// </summary>
public static class RetrievalSecurity
{
    public static DocumentRetrievalFilter Create(
        ArcKnowledgeOptions options,
        RetrievalAuthorization? authorization,
        string? requestedDealerUrn,
        string? requestedRegion,
        string? documentCategory,
        string? documentType = null)
    {
        var dealerUrn = authorization?.DealerUrn ?? requestedDealerUrn;
        if (!string.IsNullOrWhiteSpace(authorization?.DealerUrn))
            dealerUrn = authorization.DealerUrn;

        var region = authorization?.ActorRegion;
        if (string.IsNullOrWhiteSpace(region) && authorization is null)
            region = null;

        _ = requestedRegion;
        return new DocumentRetrievalFilter(
            Status: ArcKnowledgeOptions.ActiveStatus,
            Version: options.EffectivePublishedVersion(),
            Region: EmptyToNull(region),
            DealerUrn: EmptyToNull(dealerUrn),
            DocumentCategory: EmptyToNull(documentCategory),
            DocumentType: EmptyToNull(documentType));
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
