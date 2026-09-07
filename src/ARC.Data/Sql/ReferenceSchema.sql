-- Reference schema for Azure SQL (not applied automatically).
-- Authoritative transactional store per Problem Statement.

CREATE TABLE dbo.Dealer (
    Urn nvarchar(128) NOT NULL PRIMARY KEY,
    SapCode nvarchar(64) NULL,
    PortalId nvarchar(64) NULL,
    Depot nvarchar(64) NULL,
    Region nvarchar(64) NULL,
    CoveringTsi nvarchar(128) NULL,
    UnderInsolvencyMoratorium bit NOT NULL CONSTRAINT DF_Dealer_Moratorium DEFAULT (0),
    AppId nvarchar(64) NULL
);

CREATE TABLE dbo.LedgerPosition (
    Id bigint IDENTITY PRIMARY KEY,
    DealerUrn nvarchar(128) NOT NULL,
    DocumentType nvarchar(64) NOT NULL,
    DueDate date NOT NULL,
    PostedOn date NOT NULL,
    Amount decimal(18,2) NOT NULL,
    Currency char(3) NOT NULL CONSTRAINT DF_Ledger_Currency DEFAULT ('INR'),
    SourceSystem nvarchar(64) NOT NULL,
    SourceTable nvarchar(128) NOT NULL,
    SourceKey nvarchar(128) NOT NULL
);

CREATE TABLE dbo.SecurityCheque (
    DealerUrn nvarchar(128) NOT NULL,
    ChequeNumber nvarchar(64) NOT NULL,
    Micr nvarchar(64) NULL,
    Amount decimal(18,2) NOT NULL,
    Currency char(3) NOT NULL CONSTRAINT DF_Cheque_Currency DEFAULT ('INR'),
    Status nvarchar(32) NOT NULL,
    DepositDate date NULL,
    ValidityEnd date NULL,
    ExtractionConfidence decimal(4,3) NULL,
    CONSTRAINT PK_SecurityCheque PRIMARY KEY (DealerUrn, ChequeNumber)
);

CREATE TABLE dbo.ChequeReturnMemo (
    DealerUrn nvarchar(128) NOT NULL,
    ChequeNumber nvarchar(64) NOT NULL,
    ReturnReasonCode nvarchar(64) NOT NULL,
    MemoIssueDate date NOT NULL,
    MemoReceivedDate date NOT NULL,
    ExtractionConfidence decimal(4,3) NULL,
    CONSTRAINT PK_ChequeReturnMemo PRIMARY KEY (DealerUrn, ChequeNumber, MemoReceivedDate)
);

CREATE TABLE dbo.GateDecision (
    Id bigint IDENTITY PRIMARY KEY,
    CycleId nvarchar(64) NOT NULL,
    DealerUrn nvarchar(128) NOT NULL,
    GateId nvarchar(64) NOT NULL,
    ActorUpn nvarchar(256) NOT NULL,
    ActorRole nvarchar(64) NOT NULL,
    Decision nvarchar(32) NOT NULL,
    Reason nvarchar(512) NOT NULL,
    RecommendedAction nvarchar(128) NULL,
    DecidedUtc datetimeoffset NOT NULL,
    CorrelationId nvarchar(64) NOT NULL,
    WasOverride bit NOT NULL,
    CONSTRAINT UQ_GateDecision_Idempotent UNIQUE (CycleId, DealerUrn, GateId, CorrelationId)
);

CREATE TABLE dbo.LegalCase (
    DealerUrn nvarchar(128) NOT NULL PRIMARY KEY,
    CaseReference nvarchar(128) NULL,
    CompletenessScore decimal(5,4) NOT NULL,
    GapsJson nvarchar(max) NULL
);

CREATE TABLE dbo.RecoveryCaseIndex (
    CycleId nvarchar(64) NOT NULL,
    DealerUrn nvarchar(128) NOT NULL,
    Status nvarchar(64) NOT NULL,
    CorrelationId nvarchar(64) NOT NULL,
    WaitingGate nvarchar(64) NULL,
    UpdatedUtc datetimeoffset NOT NULL,
    RecoverabilityScore decimal(18,2) NULL,
    RecoveryTier nvarchar(32) NULL,
    CONSTRAINT PK_RecoveryCaseIndex PRIMARY KEY (CycleId, DealerUrn)
);
CREATE INDEX IX_RecoveryCaseIndex_Worklist
    ON dbo.RecoveryCaseIndex (CycleId, RecoverabilityScore DESC, DealerUrn)
    INCLUDE (Status, RecoveryTier, WaitingGate, CorrelationId, UpdatedUtc)
    WHERE RecoverabilityScore IS NOT NULL;

-- SP2: many source identifiers → one canonical dealer. PK prevents one source id mapping to two URNs.
CREATE TABLE dbo.DealerSourceIdentifier (
    SourceSystem nvarchar(32) NOT NULL,
    SourceIdentifier nvarchar(128) NOT NULL,
    CanonicalUrn nvarchar(128) NOT NULL,
    MatchKind nvarchar(32) NOT NULL,
    UpdatedUtc datetimeoffset NOT NULL,
    CONSTRAINT PK_DealerSourceIdentifier PRIMARY KEY (SourceSystem, SourceIdentifier),
    CONSTRAINT FK_DealerSourceIdentifier_Dealer FOREIGN KEY (CanonicalUrn) REFERENCES dbo.Dealer (Urn)
);

-- SP2 aliases: trade/legal name, cheque account name, agreement party. Same value on two dealers is Ambiguous at resolve time.
CREATE TABLE dbo.DealerAlias (
    Id bigint IDENTITY PRIMARY KEY,
    CanonicalUrn nvarchar(128) NOT NULL,
    AliasKind nvarchar(32) NOT NULL,
    AliasValue nvarchar(256) NOT NULL,
    CONSTRAINT FK_DealerAlias_Dealer FOREIGN KEY (CanonicalUrn) REFERENCES dbo.Dealer (Urn),
    CONSTRAINT UQ_DealerAlias_DealerKindValue UNIQUE (CanonicalUrn, AliasKind, AliasValue)
);
CREATE INDEX IX_DealerAlias_Value ON dbo.DealerAlias (AliasValue);

-- SP7: PTP candidate → committed lifecycle (one SoR). No raw transcript/audio.
CREATE TABLE dbo.PtpRecord (
    RecordId nvarchar(256) NOT NULL PRIMARY KEY,
    CycleId nvarchar(64) NOT NULL,
    DealerUrn nvarchar(128) NOT NULL,
    CommitmentDate date NULL,
    Amount decimal(18,2) NULL,
    Currency char(3) NOT NULL CONSTRAINT DF_PtpRecord_Currency DEFAULT ('INR'),
    Status nvarchar(32) NOT NULL,
    ConfirmedByTsi bit NOT NULL CONSTRAINT DF_PtpRecord_Confirmed DEFAULT (0),
    ConfirmedUtc datetimeoffset NULL,
    ConfirmedByUpn nvarchar(256) NULL,
    RequiresTsiConfirmation bit NOT NULL CONSTRAINT DF_PtpRecord_RequiresTsi DEFAULT (1),
    Discarded bit NOT NULL CONSTRAINT DF_PtpRecord_Discarded DEFAULT (0),
    Locale nvarchar(16) NOT NULL,
    SpeechConfidence decimal(4,3) NULL,
    TranscriptSha256 nvarchar(64) NULL,
    RecognitionStatus nvarchar(32) NULL,
    CorrelationId nvarchar(64) NULL,
    CreatedUtc datetimeoffset NOT NULL,
    UpdatedUtc datetimeoffset NOT NULL
);
CREATE INDEX IX_PtpRecord_Cycle_Status
    ON dbo.PtpRecord (CycleId, Status)
    INCLUDE (DealerUrn, CommitmentDate, ConfirmedByTsi);
CREATE INDEX IX_PtpRecord_Dealer_Status
    ON dbo.PtpRecord (DealerUrn, Status);
CREATE INDEX IX_PtpRecord_DueConfirmed
    ON dbo.PtpRecord (CommitmentDate, CycleId)
    INCLUDE (RecordId, DealerUrn)
    WHERE ConfirmedByTsi = 1 AND CommitmentDate IS NOT NULL;

-- SP7: TSI visit plan (SQL SoR). VisitTask itself remains Cosmos workflow.
CREATE TABLE dbo.VisitPlan (
    PlanId nvarchar(256) NOT NULL PRIMARY KEY,
    CycleId nvarchar(64) NOT NULL,
    TsiId nvarchar(128) NOT NULL,
    PlanDate date NOT NULL,
    CorrelationId nvarchar(64) NOT NULL,
    CreatedUtc datetimeoffset NOT NULL,
    UpdatedUtc datetimeoffset NOT NULL,
    CONSTRAINT UQ_VisitPlan_Cycle_Tsi UNIQUE (CycleId, TsiId)
);
CREATE INDEX IX_VisitPlan_Cycle ON dbo.VisitPlan (CycleId);
CREATE INDEX IX_VisitPlan_Tsi_Date ON dbo.VisitPlan (TsiId, PlanDate);

CREATE TABLE dbo.VisitPlanLine (
    PlanId nvarchar(256) NOT NULL,
    DealerUrn nvarchar(128) NOT NULL,
    Sequence int NOT NULL,
    PriorityRank int NOT NULL,
    GeoClusterId nvarchar(512) NOT NULL,
    Reason nvarchar(32) NOT NULL,
    Status nvarchar(32) NOT NULL,
    VisitTaskId nvarchar(256) NOT NULL,
    CreatedUtc datetimeoffset NOT NULL,
    CONSTRAINT PK_VisitPlanLine PRIMARY KEY (PlanId, DealerUrn),
    CONSTRAINT UQ_VisitPlanLine_Sequence UNIQUE (PlanId, Sequence),
    CONSTRAINT FK_VisitPlanLine_Plan FOREIGN KEY (PlanId) REFERENCES dbo.VisitPlan (PlanId)
);
CREATE INDEX IX_VisitPlanLine_Task ON dbo.VisitPlanLine (VisitTaskId);

-- SP7: Broken-PTP chase. No Amount column.
CREATE TABLE dbo.PtpChase (
    ChaseId nvarchar(512) NOT NULL PRIMARY KEY,
    PtpId nvarchar(256) NOT NULL,
    CycleId nvarchar(64) NOT NULL,
    DealerUrn nvarchar(128) NOT NULL,
    OwnerTsi nvarchar(128) NOT NULL,
    CommitmentDate date NOT NULL,
    DueDate date NOT NULL,
    ChaseType nvarchar(32) NOT NULL,
    Status nvarchar(32) NOT NULL,
    CorrelationId nvarchar(64) NOT NULL,
    CreatedUtc datetimeoffset NOT NULL
);
CREATE INDEX IX_PtpChase_Cycle_Status ON dbo.PtpChase (CycleId, Status);
CREATE INDEX IX_PtpChase_Dealer ON dbo.PtpChase (DealerUrn);
CREATE INDEX IX_PtpChase_Owner_Status ON dbo.PtpChase (OwnerTsi, Status);
CREATE INDEX IX_PtpChase_Ptp ON dbo.PtpChase (PtpId);

