using ARC.Domain.Entities;

namespace ARC.Domain.Metrics;

public sealed record DealerDetailTbcIndicator(string Id, string Status, string Detail);

/// <summary>
/// Chat/API-safe dealer master view. Facts originate from ODOS.usp_ARC_GetDealer only.
/// dealer_mobile is never included.
/// </summary>
public sealed record DealerDetailChatSafeContract(
    string DealerUrn,
    string DealerCode,
    string DealerName,
    string DepotCode,
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
    string? MotherAccount,
    IReadOnlyList<DealerDetailTbcIndicator> TbcIndicators,
    string DataSource);

public static class DealerDetailChatSafeAssembler
{
    public const string PublishedDataSource = "ODOS.usp_ARC_GetDealer";

    public static DealerDetailChatSafeContract FromMasterDetail(DealerMasterDetail detail)
        => new(
            detail.Urn.Value,
            detail.DealerCode,
            detail.DealerName,
            detail.DepotCode,
            detail.DepotName,
            detail.DepotRegion,
            detail.RegionName,
            detail.TerritoryCode,
            detail.TerritoryName,
            detail.SblCode,
            detail.GoldSilver,
            detail.BillTo,
            detail.PrimaryFlag,
            detail.CustomerType,
            detail.MotherAccount,
            CentralTbcCatalog(),
            PublishedDataSource);

    public static IReadOnlyList<DealerDetailTbcIndicator> CentralTbcCatalog()
        =>
        [
            new("DealerRegionMapping", "Tbc",
                "SP returns depot_regn and region; Dealer.Region uses depot_regn for authorization only."),
            new("SapCode", "Unavailable", "No ODOS production source for Dealer.SapCode."),
            new("PortalId", "Unavailable", "No ODOS production source for Dealer.PortalId."),
            new("CoveringTsi", "Unavailable", "No ODOS production source for Dealer.CoveringTsi."),
            new("UnderInsolvencyMoratorium", "Unavailable", "No ODOS production source for moratorium flag."),
            new("AppId", "Unavailable", "No ODOS production source for Dealer.AppId.")
        ];
}
