namespace ARC.Web.Services;

public sealed record BusinessChatProxyRequest(string Message, string? ConversationId);

public sealed record BusinessChatIdentityFactsDto(
    string DealerCode,
    string DealerName,
    string DepotCode,
    string DepotName,
    string? DepotRegion,
    string? RegionName,
    string? TerritoryCode,
    string? TerritoryName,
    string? BillTo,
    string? CustomerType,
    string? MotherAccount);

public sealed record BusinessChatFinancialFactsDto(
    string PeriodKey,
    decimal CurrentOutstanding,
    decimal Over90Outstanding,
    decimal OutstandingBucket0,
    decimal OutstandingBucket1,
    decimal OutstandingBucket2,
    decimal OutstandingBucket3,
    decimal OutstandingBucket4,
    decimal? BusinessLineLimit);

public sealed record BusinessChatRecoveryFactsDto(
    string? NoticeGeneratedYn,
    string? NoticeDepotYn,
    string? NoticeHoYn,
    string? NoticeDepotDate,
    string? NoticeHoDate,
    string? RecoveryStatusCode,
    string? RecoveryStatusDescription,
    string? LegalStatusCode,
    string? LegalStatusDescription);

public sealed record BusinessChatFieldFactsDto(
    decimal? PtpAmount,
    string? PtpDate,
    string? PtpStatus,
    decimal? PtpConfidence,
    string? ChaseStatus,
    string? LastVisitDate,
    string? VisitStatus,
    string? VisitOwner,
    string? VisitPlanStatus,
    int? TsiVisitCount,
    string? DealerFeedback);

public sealed record BusinessChatLegalFactsDto(
    string? ChequeNumberMasked,
    string? ChequeDate,
    decimal? ChequeAmount,
    string? ChequeStatus,
    string? ReturnMemoReason,
    string? ReturnMemoAvailability,
    bool? Section138Eligible,
    string? EligibilityReason,
    int? LimitationDaysRemaining,
    string? NoticeByDate,
    string? CureByDate,
    string? FileByDate,
    string? LegalDeadlineStatus);

public sealed record BusinessChatEvidenceFactsDto(
    decimal? CompletenessScore,
    IReadOnlyList<string>? MissingEvidence,
    string? LegalDocumentStatus,
    string? CaseReference);

public sealed record BusinessChatExposureFactsDto(
    decimal? GrossOpenAr,
    decimal? NetRecoverableExposure,
    string? ReconciliationStatus,
    IReadOnlyList<string>? MissingComponents);

public sealed record BusinessChatFactsDto(
    BusinessChatIdentityFactsDto? Identity,
    BusinessChatFinancialFactsDto? Financial,
    BusinessChatRecoveryFactsDto? Recovery,
    BusinessChatFieldFactsDto? Field = null,
    BusinessChatLegalFactsDto? Legal = null,
    BusinessChatEvidenceFactsDto? Evidence = null,
    BusinessChatExposureFactsDto? Exposure = null);

public sealed record BusinessChatProxyResponse(
    string ConversationId,
    string Answer,
    string CorrelationId,
    string RunMode,
    bool GroundedOnDeterministicFacts,
    BusinessChatFactsDto? Facts,
    string? SafetyOutcome);

public interface IBusinessChatApiClient
{
    Task<BusinessChatProxyResponse> SendAsync(BusinessChatProxyRequest request, CancellationToken cancellationToken);
}
