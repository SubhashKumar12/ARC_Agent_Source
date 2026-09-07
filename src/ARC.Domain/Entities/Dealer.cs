using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

/// <summary>Resolved dealer (SP2). Moratorium flag drives R5.</summary>
public sealed class Dealer
{
    public DealerUrn Urn { get; }
    public string? SapCode { get; }
    public string? PortalId { get; }
    public string? Depot { get; }
    public string? Region { get; }
    public string? CoveringTsi { get; }
    public string? AppId { get; }
    public bool UnderInsolvencyMoratorium { get; }

    public Dealer(
        DealerUrn urn,
        bool underInsolvencyMoratorium,
        string? sapCode = null,
        string? portalId = null,
        string? depot = null,
        string? region = null,
        string? coveringTsi = null,
        string? appId = null)
    {
        Urn = urn;
        UnderInsolvencyMoratorium = underInsolvencyMoratorium;
        SapCode = sapCode;
        PortalId = portalId;
        Depot = depot;
        Region = region;
        CoveringTsi = coveringTsi;
        AppId = appId;
    }
}
