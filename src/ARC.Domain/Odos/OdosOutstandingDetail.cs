namespace ARC.Domain.Odos;

/// <summary>
/// Provider-agnostic ODOS outstanding facts for a depot/dealer pair in a configured period.
/// Populated from approved ODOS stored procedures only.
/// </summary>
public sealed record OdosOutstandingDetail(
  string DepotCode,
  string DealerCode,
  int PeriodYear,
  int PeriodMonth,
  decimal CurrentOutstanding,
  decimal OsAmt0,
  decimal OsAmt1,
  decimal OsAmt2,
  decimal OsAmt3,
  decimal OsAmt4,
  decimal? BusinessLineLimit,
  string? BusinessLine,
  string? RecoveryStatusCode,
  string? RecoveryStatusDescription,
  string? LegalStatusCode,
  string? LegalStatusDescription,
  string? NoticeGeneratedYn,
  string? NoticeDepotYn,
  string? NoticeHoYn,
  DateOnly? NoticeDepotDate,
  DateOnly? NoticeHoDate,
  string? NoticeDepotRemarks,
  string? NoticeHoRemarks,
  string? TsiVisitStatus,
  int? TsiVisitCount,
  string? DealerFeedback,
  IReadOnlyList<string> DataSources)
{
  public decimal Over90Outstanding
    => OdosOutstandingSemantics.ComputeOver90(OsAmt1, OsAmt2, OsAmt3, OsAmt4);

  public string PeriodKey => $"{PeriodYear:D4}-{PeriodMonth:D2}";
}
