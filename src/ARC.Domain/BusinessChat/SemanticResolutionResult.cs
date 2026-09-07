namespace ARC.Domain.BusinessChat;

public enum SemanticResolutionKind
{
    Query = 0,
    DepotFollowUp = 1,
    UnsupportedCapability = 2,
    Unrecognized = 3,
}

public sealed record SemanticResolutionResult(
    SemanticResolutionKind Kind,
    IReadOnlyList<BusinessMetric> RequestedMetrics,
    IReadOnlyList<BusinessCapability> Capabilities,
    string? DealerCode,
    string? DepotCode,
    bool DealerCodeOnly,
    bool ClarificationNeeded,
    string? UnsupportedCapabilityHint,
    double Confidence,
    string ResolverSource)
{
    public static SemanticResolutionResult Unrecognized(string source = "deterministic")
        => new(
            SemanticResolutionKind.Unrecognized,
            [],
            [],
            null,
            null,
            DealerCodeOnly: false,
            ClarificationNeeded: false,
            UnsupportedCapabilityHint: null,
            Confidence: 0,
            source);

    public static SemanticResolutionResult Unsupported(string hint, string source = "deterministic")
        => new(
            SemanticResolutionKind.UnsupportedCapability,
            [],
            [],
            null,
            null,
            DealerCodeOnly: false,
            ClarificationNeeded: false,
            UnsupportedCapabilityHint: hint,
            Confidence: 1,
            source);

    public static SemanticResolutionResult DepotFollowUp(string source = "deterministic")
        => new(
            SemanticResolutionKind.DepotFollowUp,
            [BusinessMetric.DepotCode],
            [BusinessCapability.DealerIdentity],
            null,
            null,
            DealerCodeOnly: false,
            ClarificationNeeded: false,
            UnsupportedCapabilityHint: null,
            Confidence: 1,
            source);
}
