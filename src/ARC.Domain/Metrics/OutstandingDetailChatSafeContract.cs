using ARC.Domain.Odos;

namespace ARC.Domain.Metrics;

/// <summary>
/// Chat/API-safe outstanding view. Facts originate from approved ODOS read procedures only.
/// </summary>
public sealed record OutstandingDetailChatSafeContract(
  string DealerCode,
  string DepotCode,
  string? DealerName,
  string? DepotName,
  string PeriodKey,
  decimal CurrentOutstanding,
  decimal OutstandingBucket0,
  decimal OutstandingBucket1,
  decimal OutstandingBucket2,
  decimal OutstandingBucket3,
  decimal OutstandingBucket4,
  decimal Over90Outstanding,
  decimal? BusinessLineLimit,
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
  IReadOnlyList<string> DataSources);

public static class OutstandingDetailChatSafeAssembler
{
  public static OutstandingDetailChatSafeContract FromOdosDetail(
    OdosOutstandingDetail detail,
    string? dealerName = null,
    string? depotName = null)
    => new(
      detail.DealerCode,
      detail.DepotCode,
      dealerName,
      depotName,
      detail.PeriodKey,
      detail.CurrentOutstanding,
      detail.OsAmt0,
      detail.OsAmt1,
      detail.OsAmt2,
      detail.OsAmt3,
      detail.OsAmt4,
      detail.Over90Outstanding,
      detail.BusinessLineLimit,
      detail.RecoveryStatusCode,
      detail.RecoveryStatusDescription,
      detail.LegalStatusCode,
      detail.LegalStatusDescription,
      detail.NoticeGeneratedYn,
      detail.NoticeDepotYn,
      detail.NoticeHoYn,
      detail.NoticeDepotDate,
      detail.NoticeHoDate,
      detail.NoticeDepotRemarks,
      detail.NoticeHoRemarks,
      detail.TsiVisitStatus,
      detail.TsiVisitCount,
      detail.DealerFeedback,
      detail.DataSources);
}
