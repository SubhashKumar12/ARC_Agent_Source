using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Odos;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Synthetic;

/// <summary>Synthetic / Assignment Evaluation Only — dealer master row (ODOS-shaped identifiers).</summary>
public sealed record SyntheticDealerProfile(
    DealerUrn CanonicalUrn,
    string DealerCode,
    string DepotCode,
    string Region,
    string? BusinessLine,
    string? CustomerType,
    string? BillTo,
    string? MotherAccountCode,
    string? SapCode,
    string? PortalId,
    string? CoveringTsi,
    bool UnderInsolvencyMoratorium,
    SyntheticScenarioTag ScenarioTag,
    string DataClassification = SyntheticAssignmentLabels.Marker)
{
    public Dealer ToDomain() => new(
        CanonicalUrn,
        UnderInsolvencyMoratorium,
        sapCode: SapCode,
        portalId: PortalId,
        depot: DepotCode,
        region: Region,
        coveringTsi: CoveringTsi);
}

/// <summary>Synthetic / Assignment Evaluation Only — monthly opening shaped like odos_opening_data.</summary>
public sealed record SyntheticOdosOpeningRow(
    DealerUrn DealerUrn,
    OdosOpeningSnapshot Snapshot,
    string DataClassification = SyntheticAssignmentLabels.Marker);

/// <summary>Synthetic / Assignment Evaluation Only — recovery header analogue (not ODOS.odos_recovery_header).</summary>
public sealed record SyntheticRecoveryHeader(
    DealerUrn DealerUrn,
    string CycleId,
    decimal OutstandingUpdt,
    string Status,
    DateOnly AsOf,
    string DataClassification = SyntheticAssignmentLabels.Marker);

/// <summary>Synthetic / Assignment Evaluation Only — payment history row.</summary>
public sealed record SyntheticPaymentHistoryFact(
    DealerUrn DealerUrn,
    DateOnly PaymentDate,
    decimal Amount,
    string Instrument,
    string DataClassification = SyntheticAssignmentLabels.Marker);

/// <summary>Synthetic / Assignment Evaluation Only — PTP fact (may be low-confidence).</summary>
public sealed record SyntheticPtpFact(
    DealerUrn DealerUrn,
    DateOnly CommitmentDate,
    decimal Amount,
    bool ConfirmedByTsi,
    decimal Confidence,
    string DataClassification = SyntheticAssignmentLabels.Marker)
{
    public PromiseToPay ToDomain() => new(
        DealerUrn,
        CommitmentDate,
        new Money(Amount),
        ConfirmedByTsi);
}

/// <summary>Synthetic / Assignment Evaluation Only — field visit.</summary>
public sealed record SyntheticFieldVisitFact(
    DealerUrn DealerUrn,
    DateOnly VisitDate,
    string Outcome,
    string TsiId,
    string DataClassification = SyntheticAssignmentLabels.Marker);

/// <summary>Synthetic / Assignment Evaluation Only — evidence / case-file pointer.</summary>
public sealed record SyntheticEvidenceFact(
    DealerUrn DealerUrn,
    string ArtefactType,
    string Location,
    string DataClassification = SyntheticAssignmentLabels.Marker);

/// <summary>Synthetic / Assignment Evaluation Only — demand notice draft facts.</summary>
public sealed record SyntheticDemandNoticeFact(
    DealerUrn DealerUrn,
    string CycleId,
    DateOnly IssuedOn,
    decimal ClaimAmount,
    DateOnly? ServedOn,
    string DataClassification = SyntheticAssignmentLabels.Marker)
{
    public DemandNotice ToDomain() => new(
        DealerUrn,
        new CycleId(CycleId),
        IssuedOn,
        new Money(ClaimAmount),
        ServedOn);
}

/// <summary>Synthetic / Assignment Evaluation Only — Section 138 supporting facts.</summary>
public sealed record SyntheticSection138Fact(
    DealerUrn DealerUrn,
    string ChequeNumber,
    string ReturnReasonCode,
    DateOnly MemoReceived,
    DateOnly NoticeServed,
    bool QualifyingBounce,
    string DataClassification = SyntheticAssignmentLabels.Marker);

/// <summary>Synthetic / Assignment Evaluation Only — mother-account rollup edge (mstr_dlr-style).</summary>
public sealed record SyntheticMotherAccountLink(
    string ChildDealerCode,
    string MotherAccountCode,
    DealerUrn MotherCanonicalUrn,
    string DataClassification = SyntheticAssignmentLabels.Marker);

/// <summary>
/// Synthetic / Assignment Evaluation Only — R6 lineage demonstration:
/// source facts → reconciled amount → decision → drafted notice amount.
/// </summary>
public sealed record SyntheticR6LineageCase(
    DealerUrn DealerUrn,
    IReadOnlyList<LineItemRef> SourceFacts,
    decimal ReconciledNetExposure,
    string Decision,
    decimal DraftedNoticeAmount,
    string DataClassification = SyntheticAssignmentLabels.Marker);
