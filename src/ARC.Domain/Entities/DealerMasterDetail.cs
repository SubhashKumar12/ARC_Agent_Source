using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

/// <summary>
/// ODOS dealer master facts from ODOS.usp_ARC_GetDealer. Excludes dealer_mobile (PII).
/// </summary>
public sealed record DealerMasterDetail(
    DealerUrn Urn,
    string DepotCode,
    string DealerCode,
    string DealerName,
    string DepotName,
    string? DepotRegion,
    string? RegionName,
    string? TerritoryCode,
    string? TerritoryName,
    string? SblCode,
    string? GoldSilver,
    string? BillTo,
    string? PrimaryFlag,
    string? CustomerType,
    string? MotherAccount)
{
    /// <summary>
    /// Partial <see cref="Dealer"/> projection. Unsupported fields remain null/false.
    /// <see cref="Dealer.Region"/> uses <see cref="DepotRegion"/> (depot_regn) for authorization alignment; see TBC catalog.
    /// </summary>
    public Dealer ToDealer()
        => new(
            Urn,
            underInsolvencyMoratorium: false,
            sapCode: null,
            portalId: null,
            depot: DepotCode,
            region: DepotRegion,
            coveringTsi: null,
            appId: null);
}
