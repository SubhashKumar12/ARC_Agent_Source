/*
    ARC Phase 11P — ARC-owned stored procedures (deployment script).
    Source of truth: current inline SQL in src/ARC.Data/Sql/* repositories.
    Do NOT execute automatically. Review and apply to the ARC Azure SQL database only.
    ODOS objects are not modified.
*/

SET NOCOUNT ON;
GO

/* Table-valued parameter type for VisitPlan upsert line batch (repository foreach loop). */
IF TYPE_ID(N'dbo.VisitPlanLineInput') IS NULL
BEGIN
    CREATE TYPE dbo.VisitPlanLineInput AS TABLE
    (
        DealerUrn     nvarchar(128)  NOT NULL,
        Sequence      int            NOT NULL,
        PriorityRank  int            NOT NULL,
        GeoClusterId  nvarchar(512)  NOT NULL,
        Reason        nvarchar(32)   NOT NULL,
        Status        nvarchar(32)   NOT NULL,
        VisitTaskId   nvarchar(256)  NOT NULL,
        CreatedUtc    datetimeoffset NOT NULL
    );
END;
GO

-- =============================================================================
-- RecoveryCaseRepository
-- =============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_UpsertRecoveryCaseIndex
    @CycleId             nvarchar(64),
    @DealerUrn           nvarchar(128),
    @Status              nvarchar(64),
    @CorrelationId       nvarchar(64),
    @WaitingGate         nvarchar(64) = NULL,
    @UpdatedUtc          datetimeoffset,
    @RecoverabilityScore decimal(18, 2) = NULL,
    @RecoveryTier        nvarchar(32) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.RecoveryCaseIndex AS t
    USING (SELECT @CycleId AS CycleId, @DealerUrn AS DealerUrn) AS s
    ON t.CycleId = s.CycleId AND t.DealerUrn = s.DealerUrn
    WHEN MATCHED THEN UPDATE SET
        Status = @Status,
        CorrelationId = @CorrelationId,
        WaitingGate = @WaitingGate,
        UpdatedUtc = @UpdatedUtc,
        RecoverabilityScore = COALESCE(@RecoverabilityScore, t.RecoverabilityScore),
        RecoveryTier = COALESCE(@RecoveryTier, t.RecoveryTier)
    WHEN NOT MATCHED THEN INSERT
        (CycleId, DealerUrn, Status, CorrelationId, WaitingGate, UpdatedUtc, RecoverabilityScore, RecoveryTier)
        VALUES (@CycleId, @DealerUrn, @Status, @CorrelationId, @WaitingGate, @UpdatedUtc, @RecoverabilityScore, @RecoveryTier);
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetRecoveryCaseIndex
    @CycleId   nvarchar(64),
    @DealerUrn nvarchar(128)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CycleId, DealerUrn, Status, CorrelationId, WaitingGate, UpdatedUtc, RecoverabilityScore, RecoveryTier
    FROM dbo.RecoveryCaseIndex
    WHERE CycleId = @CycleId AND DealerUrn = @DealerUrn;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ListRecoveryCasesByCycle
    @CycleId nvarchar(64),
    @Region  nvarchar(64) = NULL,
    @Depot   nvarchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT i.CycleId, i.DealerUrn, i.Status, i.CorrelationId, i.WaitingGate, i.UpdatedUtc, i.RecoverabilityScore, i.RecoveryTier
    FROM dbo.RecoveryCaseIndex i
    INNER JOIN dbo.Dealer d ON d.Urn = i.DealerUrn
    WHERE i.CycleId = @CycleId
      AND (@Region IS NULL OR d.Region = @Region)
      AND (@Depot IS NULL OR d.Depot = @Depot)
    ORDER BY i.UpdatedUtc DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetRankedWorklist
    @CycleId   nvarchar(64),
    @Region    nvarchar(64) = NULL,
    @Depot     nvarchar(64) = NULL,
    @TopDecile bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    WITH eligible AS (
        SELECT
            i.DealerUrn,
            i.RecoverabilityScore,
            i.RecoveryTier,
            i.Status,
            i.WaitingGate,
            ROW_NUMBER() OVER (
                ORDER BY i.RecoverabilityScore DESC, i.DealerUrn COLLATE Latin1_General_BIN2 ASC) AS RankNo,
            COUNT(*) OVER () AS EligibleCount
        FROM dbo.RecoveryCaseIndex i
        INNER JOIN dbo.Dealer d ON d.Urn = i.DealerUrn
        WHERE i.CycleId = @CycleId
          AND i.RecoverabilityScore IS NOT NULL
          AND i.RecoveryTier IS NOT NULL
          AND i.Status NOT IN (N'Blocked', N'Failed')
          AND (@Region IS NULL OR d.Region = @Region)
          AND (@Depot IS NULL OR d.Depot = @Depot)
    )
    SELECT DealerUrn, RecoverabilityScore, RecoveryTier, Status, WaitingGate, RankNo, EligibleCount
    FROM eligible
    WHERE @TopDecile = 0
       OR RankNo <= CASE
            WHEN EligibleCount = 0 THEN 0
            ELSE CEILING(CAST(EligibleCount AS decimal(18, 4)) * 0.10)
          END
    ORDER BY RankNo;
END;
GO

-- =============================================================================
-- DealerIdentityMappingRepository
-- =============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_GetDealerSourceMapping
    @SourceSystem     nvarchar(32),
    @SourceIdentifier nvarchar(128)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT SourceSystem, SourceIdentifier, CanonicalUrn, MatchKind
    FROM dbo.DealerSourceIdentifier
    WHERE SourceSystem = @SourceSystem AND SourceIdentifier = @SourceIdentifier;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_SaveResolvedMapping
    @SourceSystem     nvarchar(32),
    @SourceIdentifier nvarchar(128),
    @CanonicalUrn     nvarchar(128),
    @MatchKind        nvarchar(32)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Existing nvarchar(128);
    SELECT @Existing = CanonicalUrn
    FROM dbo.DealerSourceIdentifier WITH (UPDLOCK, HOLDLOCK)
    WHERE SourceSystem = @SourceSystem AND SourceIdentifier = @SourceIdentifier;

    IF @Existing IS NOT NULL AND @Existing <> @CanonicalUrn
    BEGIN
        THROW 50001, N'Source identifier is already bound to a different canonical dealer.', 1;
    END

    IF @Existing IS NULL
        INSERT INTO dbo.DealerSourceIdentifier
            (SourceSystem, SourceIdentifier, CanonicalUrn, MatchKind, UpdatedUtc)
        VALUES
            (@SourceSystem, @SourceIdentifier, @CanonicalUrn, @MatchKind, SYSUTCDATETIME());
    ELSE
        UPDATE dbo.DealerSourceIdentifier
        SET MatchKind = @MatchKind, UpdatedUtc = SYSUTCDATETIME()
        WHERE SourceSystem = @SourceSystem AND SourceIdentifier = @SourceIdentifier;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_FindDealerUrnsByIdentifier
    @SourceSystem     nvarchar(32),
    @SourceIdentifier nvarchar(128)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Urn
    FROM dbo.Dealer
    WHERE @SourceSystem = N'SAP'
      AND SapCode IS NOT NULL
      AND UPPER(LTRIM(RTRIM(SapCode))) = @SourceIdentifier
    UNION
    SELECT Urn
    FROM dbo.Dealer
    WHERE @SourceSystem = N'PORTAL'
      AND PortalId IS NOT NULL
      AND UPPER(LTRIM(RTRIM(PortalId))) = @SourceIdentifier
    UNION
    SELECT Urn
    FROM dbo.Dealer
    WHERE @SourceSystem = N'FIELD-APP'
      AND AppId IS NOT NULL
      AND UPPER(LTRIM(RTRIM(AppId))) = @SourceIdentifier;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_FindDealerUrnsByAlias
    @AliasValue nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT DISTINCT CanonicalUrn
    FROM dbo.DealerAlias
    WHERE AliasValue = @AliasValue;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_SaveDealerAlias
    @CanonicalUrn nvarchar(128),
    @AliasKind    nvarchar(32),
    @AliasValue   nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.DealerAlias
        WHERE CanonicalUrn = @CanonicalUrn AND AliasKind = @AliasKind AND AliasValue = @AliasValue)
    INSERT INTO dbo.DealerAlias (CanonicalUrn, AliasKind, AliasValue)
    VALUES (@CanonicalUrn, @AliasKind, @AliasValue);
END;
GO

-- =============================================================================
-- GateDecisionRepository (append-only ARC audit — not ODOS)
-- =============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_SaveGateDecision
    @CycleId            nvarchar(64),
    @DealerUrn          nvarchar(128),
    @GateId             nvarchar(64),
    @ActorUpn           nvarchar(256),
    @ActorRole          nvarchar(64),
    @Decision           nvarchar(32),
    @Reason             nvarchar(512),
    @RecommendedAction  nvarchar(128) = NULL,
    @DecidedUtc         datetimeoffset,
    @CorrelationId      nvarchar(64),
    @WasOverride        bit
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.GateDecision
        WHERE CycleId = @CycleId AND DealerUrn = @DealerUrn
          AND GateId = @GateId AND CorrelationId = @CorrelationId)
    INSERT INTO dbo.GateDecision
        (CycleId, DealerUrn, GateId, ActorUpn, ActorRole, Decision, Reason,
         RecommendedAction, DecidedUtc, CorrelationId, WasOverride)
    VALUES
        (@CycleId, @DealerUrn, @GateId, @ActorUpn, @ActorRole, @Decision, @Reason,
         @RecommendedAction, @DecidedUtc, @CorrelationId, @WasOverride);
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ListGateDecisions
    @CycleId   nvarchar(64),
    @DealerUrn nvarchar(128)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT GateId, ActorUpn, ActorRole, Decision, Reason, RecommendedAction, DecidedUtc, CorrelationId
    FROM dbo.GateDecision
    WHERE CycleId = @CycleId AND DealerUrn = @DealerUrn
    ORDER BY DecidedUtc;
END;
GO

-- =============================================================================
-- SqlPtpRecordRepository
-- =============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_UpsertPtpRecord
    @RecordId                 nvarchar(256),
    @CycleId                  nvarchar(64),
    @DealerUrn                nvarchar(128),
    @CommitmentDate           date = NULL,
    @Amount                   decimal(18, 2) = NULL,
    @Currency                 char(3),
    @Status                   nvarchar(32),
    @ConfirmedByTsi           bit,
    @ConfirmedUtc             datetimeoffset = NULL,
    @ConfirmedByUpn           nvarchar(256) = NULL,
    @RequiresTsiConfirmation  bit,
    @Discarded                bit,
    @Locale                   nvarchar(16),
    @SpeechConfidence         decimal(4, 3) = NULL,
    @TranscriptSha256         nvarchar(64) = NULL,
    @RecognitionStatus        nvarchar(32) = NULL,
    @CorrelationId            nvarchar(64) = NULL,
    @CreatedUtc               datetimeoffset,
    @UpdatedUtc               datetimeoffset
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.PtpRecord AS t
    USING (SELECT @RecordId AS RecordId) AS s
    ON t.RecordId = s.RecordId
    WHEN MATCHED THEN UPDATE SET
        CycleId = @CycleId,
        DealerUrn = @DealerUrn,
        CommitmentDate = @CommitmentDate,
        Amount = @Amount,
        Currency = @Currency,
        Status = @Status,
        ConfirmedByTsi = @ConfirmedByTsi,
        ConfirmedUtc = @ConfirmedUtc,
        ConfirmedByUpn = @ConfirmedByUpn,
        RequiresTsiConfirmation = @RequiresTsiConfirmation,
        Discarded = @Discarded,
        Locale = @Locale,
        SpeechConfidence = @SpeechConfidence,
        TranscriptSha256 = @TranscriptSha256,
        RecognitionStatus = @RecognitionStatus,
        CorrelationId = @CorrelationId,
        UpdatedUtc = @UpdatedUtc
    WHEN NOT MATCHED THEN INSERT
        (RecordId, CycleId, DealerUrn, CommitmentDate, Amount, Currency, Status,
         ConfirmedByTsi, ConfirmedUtc, ConfirmedByUpn, RequiresTsiConfirmation, Discarded,
         Locale, SpeechConfidence, TranscriptSha256, RecognitionStatus, CorrelationId,
         CreatedUtc, UpdatedUtc)
        VALUES
        (@RecordId, @CycleId, @DealerUrn, @CommitmentDate, @Amount, @Currency, @Status,
         @ConfirmedByTsi, @ConfirmedUtc, @ConfirmedByUpn, @RequiresTsiConfirmation, @Discarded,
         @Locale, @SpeechConfidence, @TranscriptSha256, @RecognitionStatus, @CorrelationId,
         @CreatedUtc, @UpdatedUtc);
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetPtpRecord
    @RecordId nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT RecordId, CycleId, DealerUrn, CommitmentDate, Amount, Currency, Status,
           ConfirmedByTsi, ConfirmedUtc, ConfirmedByUpn, RequiresTsiConfirmation, Discarded,
           Locale, SpeechConfidence, TranscriptSha256, RecognitionStatus, CorrelationId,
           CreatedUtc, UpdatedUtc
    FROM dbo.PtpRecord
    WHERE RecordId = @RecordId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ListPtpCandidatesByCycleDealer
    @CycleId nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT RecordId, CycleId, DealerUrn, CommitmentDate, Amount, Currency, Status,
           ConfirmedByTsi, ConfirmedUtc, ConfirmedByUpn, RequiresTsiConfirmation, Discarded,
           Locale, SpeechConfidence, TranscriptSha256, RecognitionStatus, CorrelationId,
           CreatedUtc, UpdatedUtc
    FROM dbo.PtpRecord
    WHERE CycleId = @CycleId
      AND ConfirmedByTsi = 1
      AND Status = N'Confirmed'
    ORDER BY CommitmentDate, RecordId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ListPtpCommittedByDealer
    @DealerUrn nvarchar(128)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT RecordId, CycleId, DealerUrn, CommitmentDate, Amount, Currency, Status,
           ConfirmedByTsi, ConfirmedUtc, ConfirmedByUpn, RequiresTsiConfirmation, Discarded,
           Locale, SpeechConfidence, TranscriptSha256, RecognitionStatus, CorrelationId,
           CreatedUtc, UpdatedUtc
    FROM dbo.PtpRecord
    WHERE DealerUrn = @DealerUrn
      AND ConfirmedByTsi = 1
      AND Status = N'Confirmed'
    ORDER BY CommitmentDate, RecordId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ListPtpCandidatesByStatus
    @AsOf date
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.RecordId, p.CycleId, p.DealerUrn, p.CommitmentDate, p.Amount, p.Currency, p.Status,
           p.ConfirmedByTsi, p.ConfirmedUtc, p.ConfirmedByUpn, p.RequiresTsiConfirmation, p.Discarded,
           p.Locale, p.SpeechConfidence, p.TranscriptSha256, p.RecognitionStatus, p.CorrelationId,
           p.CreatedUtc, p.UpdatedUtc
    FROM dbo.PtpRecord p
    WHERE p.ConfirmedByTsi = 1
      AND p.Status = N'Confirmed'
      AND p.CommitmentDate IS NOT NULL
      AND p.CommitmentDate < @AsOf
      AND NOT EXISTS (
          SELECT 1 FROM dbo.PtpChase c
          WHERE c.PtpId = p.RecordId
            AND c.ChaseType = N'Broken'
            AND c.CommitmentDate = p.CommitmentDate)
    ORDER BY p.CommitmentDate, p.RecordId;
END;
GO

-- =============================================================================
-- SqlVisitPlanRepository
-- =============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_UpsertVisitPlan
    @PlanId        nvarchar(256),
    @CycleId       nvarchar(64),
    @TsiId         nvarchar(128),
    @PlanDate      date,
    @CorrelationId nvarchar(64),
    @CreatedUtc    datetimeoffset,
    @UpdatedUtc    datetimeoffset,
    @Lines         dbo.VisitPlanLineInput READONLY
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        MERGE dbo.VisitPlan AS t
        USING (SELECT @PlanId AS PlanId) AS s
        ON t.PlanId = s.PlanId
        WHEN MATCHED THEN UPDATE SET
            CycleId = @CycleId,
            TsiId = @TsiId,
            PlanDate = @PlanDate,
            CorrelationId = @CorrelationId,
            UpdatedUtc = @UpdatedUtc
        WHEN NOT MATCHED THEN INSERT
            (PlanId, CycleId, TsiId, PlanDate, CorrelationId, CreatedUtc, UpdatedUtc)
            VALUES (@PlanId, @CycleId, @TsiId, @PlanDate, @CorrelationId, @CreatedUtc, @UpdatedUtc);

        DELETE FROM dbo.VisitPlanLine WHERE PlanId = @PlanId;

        INSERT INTO dbo.VisitPlanLine
            (PlanId, DealerUrn, Sequence, PriorityRank, GeoClusterId, Reason, Status, VisitTaskId, CreatedUtc)
        SELECT @PlanId, DealerUrn, Sequence, PriorityRank, GeoClusterId, Reason, Status, VisitTaskId, CreatedUtc
        FROM @Lines
        ORDER BY Sequence;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetVisitPlan
    @PlanId nvarchar(256)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT PlanId, CycleId, TsiId, PlanDate, CorrelationId, CreatedUtc, UpdatedUtc
    FROM dbo.VisitPlan
    WHERE PlanId = @PlanId;

    SELECT PlanId, DealerUrn, Sequence, PriorityRank, GeoClusterId, Reason, Status, VisitTaskId, CreatedUtc
    FROM dbo.VisitPlanLine
    WHERE PlanId = @PlanId
    ORDER BY Sequence;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ListVisitPlansByCycleTsi
    @CycleId nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT PlanId, CycleId, TsiId, PlanDate, CorrelationId, CreatedUtc, UpdatedUtc
    FROM dbo.VisitPlan
    WHERE CycleId = @CycleId
    ORDER BY TsiId;

    SELECT l.PlanId, l.DealerUrn, l.Sequence, l.PriorityRank, l.GeoClusterId, l.Reason, l.Status, l.VisitTaskId, l.CreatedUtc
    FROM dbo.VisitPlanLine l
    INNER JOIN dbo.VisitPlan p ON p.PlanId = l.PlanId
    WHERE p.CycleId = @CycleId
    ORDER BY l.PlanId, l.Sequence;
END;
GO

-- =============================================================================
-- SqlPtpChaseRepository
-- =============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_UpsertPtpChase
    @ChaseId         nvarchar(512),
    @PtpId           nvarchar(256),
    @CycleId         nvarchar(64),
    @DealerUrn       nvarchar(128),
    @OwnerTsi        nvarchar(128),
    @CommitmentDate  date,
    @DueDate         date,
    @ChaseType       nvarchar(32),
    @Status          nvarchar(32),
    @CorrelationId   nvarchar(64),
    @CreatedUtc      datetimeoffset
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.PtpChase WHERE ChaseId = @ChaseId)
    INSERT INTO dbo.PtpChase
        (ChaseId, PtpId, CycleId, DealerUrn, OwnerTsi, CommitmentDate, DueDate,
         ChaseType, Status, CorrelationId, CreatedUtc)
    VALUES
        (@ChaseId, @PtpId, @CycleId, @DealerUrn, @OwnerTsi, @CommitmentDate, @DueDate,
         @ChaseType, @Status, @CorrelationId, @CreatedUtc);
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetPtpChase
    @ChaseId nvarchar(512)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ChaseId, PtpId, CycleId, DealerUrn, OwnerTsi, CommitmentDate, DueDate,
           ChaseType, Status, CorrelationId, CreatedUtc
    FROM dbo.PtpChase
    WHERE ChaseId = @ChaseId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ListPtpChasesByCycleStatus
    @CycleId nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ChaseId, PtpId, CycleId, DealerUrn, OwnerTsi, CommitmentDate, DueDate,
           ChaseType, Status, CorrelationId, CreatedUtc
    FROM dbo.PtpChase
    WHERE CycleId = @CycleId
    ORDER BY CreatedUtc;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ListPtpChasesByTsiStatus
    @Status nvarchar(32)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ChaseId, PtpId, CycleId, DealerUrn, OwnerTsi, CommitmentDate, DueDate,
           ChaseType, Status, CorrelationId, CreatedUtc
    FROM dbo.PtpChase
    WHERE Status = @Status
    ORDER BY CreatedUtc;
END;
GO
