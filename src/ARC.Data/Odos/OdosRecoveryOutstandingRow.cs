namespace ARC.Data.Odos;

/// <summary>
/// Outstanding and recovery header subset from ODOS.usp_GetDealerDetails (documented columns).
/// </summary>
internal sealed class OdosRecoveryOutstandingRow
{
  public long? legal_id { get; set; }
  public string? dlr_dealer_code { get; set; }
  public string? depot_code { get; set; }
  public decimal? os_amt_updt { get; set; }
  public decimal? os_amt0 { get; set; }
  public decimal? os_amt1 { get; set; }
  public decimal? os_amt2 { get; set; }
  public decimal? os_amt3 { get; set; }
  public decimal? os_amt4 { get; set; }
  public string? dlr_sbl { get; set; }
  public decimal? business_line_limit { get; set; }
  public string? tsi_visit_status { get; set; }
  public int? tsi_visit_count { get; set; }
  public string? dealer_feedback { get; set; }
  public string? current_status_code { get; set; }
  public string? current_status { get; set; }
  public string? legal_status_code { get; set; }
  public string? legal_status { get; set; }
  public string? notice_yn { get; set; }
  public string? notice_yn_ho { get; set; }
  public string? notice_desc { get; set; }
  public string? notice_desc_ho { get; set; }
  public DateTime? notice_date { get; set; }
  public DateTime? notice_date_ho { get; set; }
  public string? demand_notice_generated_yn { get; set; }
}
