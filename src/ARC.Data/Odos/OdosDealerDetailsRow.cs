namespace ARC.Data.Odos;

/// <summary>
/// Header subset from ODOS.usp_GetDealerDetails for legal-case read (documented columns).
/// </summary>
internal sealed class OdosDealerDetailsRow
{
    public long? legal_id { get; set; }
    public string? dlr_dealer_code { get; set; }
    public string? depot_code { get; set; }
    public string? depot_regn { get; set; }
    public string? region { get; set; }
    public string? current_status_code { get; set; }
    public string? legal_status_code { get; set; }
    public decimal? os_amt_updt { get; set; }
}
