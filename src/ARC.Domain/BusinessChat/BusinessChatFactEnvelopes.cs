namespace ARC.Domain.BusinessChat;

/// <summary>Proven dealer master facts from ODOS.usp_ARC_GetDealer.</summary>
public sealed record DealerIdentityFacts(
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
    string? MotherAccount,
    string? SblCode,
    string? GoldSilver,
    string? PrimaryFlag);

/// <summary>Proven outstanding/ageing facts from ODOS opening/recovery reads.</summary>
public sealed record DealerFinancialFacts(
    string PeriodKey,
    decimal CurrentOutstanding,
    decimal OutstandingBucket0,
    decimal OutstandingBucket1,
    decimal OutstandingBucket2,
    decimal OutstandingBucket3,
    decimal OutstandingBucket4,
    decimal Over90Outstanding,
    decimal? BusinessLineLimit);

/// <summary>Proven recovery/notice facts from ODOS.usp_GetDealerDetails header.</summary>
public sealed record DealerRecoveryFacts(
    string? NoticeGeneratedYn,
    string? NoticeDepotYn,
    string? NoticeHoYn,
    DateOnly? NoticeDepotDate,
    DateOnly? NoticeHoDate,
    string? NoticeDepotRemarks,
    string? NoticeHoRemarks,
    string? RecoveryStatusCode,
    string? RecoveryStatusDescription,
    string? LegalStatusCode,
    string? LegalStatusDescription);

/// <summary>PTP / visit / chase facts from ARC SP7 read composition.</summary>
public sealed record DealerFieldFacts(
    decimal? PtpAmount,
    DateOnly? PtpDate,
    string? PtpStatus,
    decimal? PtpConfidence,
    string? ChaseStatus,
    DateOnly? LastVisitDate,
    string? VisitStatus,
    string? VisitOwner,
    string? VisitPlanStatus,
    int? TsiVisitCount,
    string? DealerFeedback);

/// <summary>Security cheque + Section 138 / limitation facts from deterministic A4 composition.</summary>
public sealed record DealerLegalFacts(
    string? ChequeNumberMasked,
    DateOnly? ChequeDate,
    decimal? ChequeAmount,
    string? ChequeStatus,
    string? ReturnMemoReason,
    string? ReturnMemoAvailability,
    bool? Section138Eligible,
    string? EligibilityReason,
    int? LimitationDaysRemaining,
    DateOnly? NoticeByDate,
    DateOnly? CureByDate,
    DateOnly? FileByDate,
    string? LegalDeadlineStatus,
    IReadOnlyList<string> TbcIndicators);

/// <summary>Evidence / case-file completeness from A7 / LegalCase read.</summary>
public sealed record DealerEvidenceFacts(
    decimal? CompletenessScore,
    IReadOnlyList<string> MissingEvidence,
    string? LegalDocumentStatus,
    string? CaseReference);

/// <summary>A1 net exposure with explicit missing-component disclosure.</summary>
public sealed record DealerExposureFacts(
    decimal? GrossOpenAr,
    decimal? NetRecoverableExposure,
    string? ReconciliationStatus,
    IReadOnlyList<string> MissingComponents);

public sealed record MetricUnavailability(BusinessMetric Metric, string Reason);

/// <summary>Unified fact bundle for chat response composition.</summary>
public sealed record BusinessChatFactBundle(
    string DealerCode,
    string DepotCode,
    DealerIdentityFacts? Identity,
    DealerFinancialFacts? Financial,
    DealerRecoveryFacts? Recovery,
    DealerFieldFacts? Field = null,
    DealerLegalFacts? Legal = null,
    DealerEvidenceFacts? Evidence = null,
    DealerExposureFacts? Exposure = null,
    IReadOnlyList<MetricUnavailability>? UnavailableMetrics = null);
