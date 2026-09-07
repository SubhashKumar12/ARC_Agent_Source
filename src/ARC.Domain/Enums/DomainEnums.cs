namespace ARC.Domain.Enums;

public enum NoticeDecision
{
    Issue = 0,
    Hold = 1,
    Reconcile = 2
}

public enum RecoveryTier
{
    Visit = 0,
    Notice = 1,
    Section138 = 2
}

public enum ReconciliationStatus
{
    Reconciled = 0,
    Unreconciled = 1
}

public enum WorkflowStatus
{
    Running = 0,
    WaitingForHuman = 1,
    Completed = 2,
    Failed = 3,
    Blocked = 4,
    Terminated = 5
}

public enum RunMode
{
    Shadow = 0,
    Assisted = 1,
    Live = 2
}

public enum ClockStatus
{
    Healthy = 0,
    Warning = 1,
    Critical = 2,
    Expired = 3
}

public enum ClockAlertKind
{
    T10 = 10,
    T5 = 5,
    T2 = 2
}

/// <summary>
/// Which memo date anchors the statutory clock.
/// To Be Confirmed with Legal — do not bake into prompts.
/// </summary>
public enum ClockAnchorKind
{
    MemoReceivedDate = 0,
    MemoIssueDate = 1
}

public enum ChequeStatus
{
    OnFile = 0,
    Deposited = 1,
    Realised = 2,
    Bounced = 3
}

public enum DisputeStatus
{
    None = 0,
    UnderReview = 1,
    Closed = 2
}

public enum DocumentType
{
    LedgerExtract = 0,
    Invoice = 1,
    CreditNote = 2,
    DealerAgreement = 3,
    SecurityChequeImage = 4,
    ChequeReturnMemo = 5,
    DemandNotice = 6,
    DeliveryProof = 7,
    CourierPod = 8,
    ServiceProof = 9,
    FieldVisitRecord = 10,
    Section138Notice = 11,
    CaseFileBundle = 12
}

public enum GateId
{
    DepotManager = 0,
    AdvocateSignature = 1,
    LegalProgression = 2,
    LegalCaseFileReview = 3
}

public enum GateDecisionStatus
{
    Approved = 0,
    Declined = 1,
    Expired = 2
}

public enum ActorRole
{
    DepotManager = 0,
    Advocate = 1,
    Legal = 2,
    Tsi = 3,
    DepotAdmin = 4,
    Finance = 5,
    /// <summary>Recommending agent — R4: must never approve.</summary>
    Agent = 6
}

public enum DealerIdentityStatus
{
    Resolved = 0,
    Unresolved = 1,
    Ambiguous = 2
}

public enum DealerMatchKind
{
    ExactIdentifier = 0,
    ExactAlias = 1,
    Manual = 2,
    ExistingMapping = 3
}

public enum RuleSet
{
    NoticeEligibility = 0,
    Section138Eligibility = 1,
    LimitationClock = 2,
    Governance = 3,
    AllActions = 4
}
