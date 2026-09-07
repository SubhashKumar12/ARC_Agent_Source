namespace ARC.Domain.BusinessChat;

/// <summary>
/// Approved business metrics mapped to proven ODOS/ARC source fields.
/// </summary>
public enum BusinessMetric
{
    DealerName = 1,
    DealerCode = 2,
    DepotCode = 3,
    DepotName = 4,
    Region = 5,
    Territory = 6,
    BillTo = 7,
    CustomerType = 8,
    MotherAccount = 9,

    CurrentOutstanding = 20,
    AgeingBucket0 = 21,
    AgeingBucket1 = 22,
    AgeingBucket2 = 23,
    AgeingBucket3 = 24,
    AgeingBucket4 = 25,
    Over90Outstanding = 26,
    BusinessLineLimit = 27,

    NoticeGenerated = 40,
    NoticeDepotStatus = 41,
    NoticeHoStatus = 42,
    NoticeDate = 43,
    RecoveryStatus = 44,
    LegalStatus = 45,

    PtpAmount = 60,
    PtpDate = 61,
    PtpStatus = 62,
    PtpConfidence = 63,
    ChaseStatus = 64,
    LastVisitDate = 65,
    VisitStatus = 66,
    VisitOwner = 67,
    VisitPlanStatus = 68,
    TsiVisitCount = 69,
    DealerFeedback = 70,

    ChequeNumberMasked = 80,
    ChequeDate = 81,
    ChequeAmount = 82,
    ChequeStatus = 83,
    ReturnMemoReason = 84,

    Section138Eligibility = 100,
    EligibilityReason = 101,
    LimitationDaysRemaining = 102,
    NoticeByDate = 103,
    CureByDate = 104,
    FileByDate = 105,
    LegalDeadlineStatus = 106,

    EvidenceCompleteness = 120,
    MissingEvidence = 121,
    LegalDocumentStatus = 122,

    NetRecoverableExposure = 140,
    ExposureMissingComponents = 141,
    GrossOpenAr = 142,
}
