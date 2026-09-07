namespace ARC.Domain.Odos;

/// <summary>
/// Documented ODOS ageing bucket semantics from supplied ODOS inventory
/// (<c>015_usp_GetDealerDetails_os_amt_columns.sql</c> / ODOS_Chatbot_Data_Inventory §5).
/// ARC does not recalculate ageing from dates.
/// </summary>
public static class OdosOutstandingSemantics
{
  public const string Bucket0Label = "< 90 days";
  public const string Bucket1Label = "90–120 days";
  public const string Bucket2Label = "121–180 days";
  public const string Bucket3Label = "181–365 days";
  public const string Bucket4Label = "> 365 days";

  /// <summary>
  /// ODOS definition: outstanding above 90 days = sum of buckets 1–4 (excludes bucket 0).
  /// </summary>
  public static decimal ComputeOver90(decimal osAmt1, decimal osAmt2, decimal osAmt3, decimal osAmt4)
    => osAmt1 + osAmt2 + osAmt3 + osAmt4;

  public static IReadOnlyList<(string Label, decimal Amount)> BucketBreakdown(
    decimal osAmt0,
    decimal osAmt1,
    decimal osAmt2,
    decimal osAmt3,
    decimal osAmt4)
    =>
    [
      (Bucket0Label, osAmt0),
      (Bucket1Label, osAmt1),
      (Bucket2Label, osAmt2),
      (Bucket3Label, osAmt3),
      (Bucket4Label, osAmt4),
    ];
}
