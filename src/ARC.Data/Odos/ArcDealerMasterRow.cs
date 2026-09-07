using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Odos;

/// <summary>
/// Row shape for ODOS.usp_ARC_GetDealer (published columns only).
/// </summary>
internal sealed class ArcDealerMasterRow
{
    public string? depot_code { get; set; }
    public string? dealer_code { get; set; }
    public string? dealer_name { get; set; }
    public string? depot_name { get; set; }
    public string? depot_regn { get; set; }
    public string? region { get; set; }
    public string? terr_code { get; set; }
    public string? terr_name { get; set; }
    public string? sbl_code { get; set; }
    public string? gold_silver { get; set; }
    public string? bill_to { get; set; }
    public string? primary_flag { get; set; }
    public string? cust_type { get; set; }
    public string? mother_acc { get; set; }

    // Present on SP result; never projected to domain or chat contracts.
    public string? dealer_mobile { get; set; }

    public DealerMasterDetail? ToMasterDetail(DealerUrn urn)
    {
        if (string.IsNullOrWhiteSpace(depot_code) || string.IsNullOrWhiteSpace(dealer_code))
            return null;

        return new DealerMasterDetail(
            urn,
            depot_code.Trim(),
            dealer_code.Trim(),
            dealer_name?.Trim() ?? string.Empty,
            depot_name?.Trim() ?? string.Empty,
            NullIfEmpty(depot_regn),
            NullIfEmpty(region),
            NullIfEmpty(terr_code),
            NullIfEmpty(terr_name),
            NullIfEmpty(sbl_code),
            NullIfEmpty(gold_silver),
            NullIfEmpty(bill_to),
            NullIfEmpty(primary_flag),
            NullIfEmpty(cust_type),
            NullIfEmpty(mother_acc));
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
