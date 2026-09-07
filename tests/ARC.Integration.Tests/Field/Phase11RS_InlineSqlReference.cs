using Dapper;
using Microsoft.Data.SqlClient;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;

namespace ARC.Integration.Tests.Field;

/// <summary>
/// Reference inline SQL copied from pre-11RS repositories for parity comparison only.
/// </summary>
internal static class Phase11RS_InlineSqlReference
{
    public static async Task SaveGateDecisionInlineAsync(
        string connectionString,
        CycleId cycleId,
        DealerUrn dealerUrn,
        GateDecision decision,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (
                SELECT 1 FROM dbo.GateDecision
                WHERE CycleId = @CycleId AND DealerUrn = @DealerUrn
                  AND GateId = @GateId AND CorrelationId = @CorrelationId)
            INSERT INTO dbo.GateDecision
                (CycleId, DealerUrn, GateId, ActorUpn, ActorRole, Decision, Reason,
                 RecommendedAction, DecidedUtc, CorrelationId, WasOverride)
            VALUES
                (@CycleId, @DealerUrn, @GateId, @ActorUpn, @ActorRole, @Decision, @Reason,
                 @RecommendedAction, @DecidedUtc, @CorrelationId, @WasOverride)
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            CycleId = cycleId.Value,
            DealerUrn = dealerUrn.Value,
            GateId = decision.Gate.ToString(),
            decision.ActorUpn,
            ActorRole = decision.ActorRole.ToString(),
            Decision = decision.Decision.ToString(),
            decision.Reason,
            decision.RecommendedAction,
            DecidedUtc = decision.DecidedUtc,
            CorrelationId = decision.CorrelationId.Value,
            decision.WasOverride
        }, cancellationToken: cancellationToken));
    }

    public static async Task<int> CountGateDecisionsAsync(
        string connectionString, CycleId cycleId, DealerUrn dealerUrn, string correlationId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.GateDecision WHERE CycleId = @CycleId AND DealerUrn = @DealerUrn AND CorrelationId = @CorrelationId",
            new { CycleId = cycleId.Value, DealerUrn = dealerUrn.Value, CorrelationId = correlationId },
            cancellationToken: ct));
    }

    public static async Task SaveAliasInlineAsync(
        string connectionString, DealerUrn urn, string aliasKind, string aliasValue, CancellationToken ct)
    {
        const string sql = """
            IF NOT EXISTS (
                SELECT 1 FROM dbo.DealerAlias
                WHERE CanonicalUrn = @CanonicalUrn AND AliasKind = @AliasKind AND AliasValue = @AliasValue)
            INSERT INTO dbo.DealerAlias (CanonicalUrn, AliasKind, AliasValue)
            VALUES (@CanonicalUrn, @AliasKind, @AliasValue)
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            CanonicalUrn = urn.Value,
            AliasKind = aliasKind,
            AliasValue = DealerIdentityNormalization.Alias(aliasValue)
        }, cancellationToken: ct));
    }

    public static async Task SaveResolvedMappingInlineAsync(
        string connectionString, DealerSourceMapping mapping, CancellationToken ct)
    {
        const string sql = """
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
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            mapping.SourceSystem,
            mapping.SourceIdentifier,
            CanonicalUrn = mapping.CanonicalUrn.Value,
            MatchKind = mapping.MatchKind.ToString()
        }, cancellationToken: ct));
    }

    public static async Task<bool> TryInsertChaseInlineAsync(
        string connectionString, PtpChaseEntity chase, CancellationToken ct)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.PtpChase WHERE ChaseId = @ChaseId)
            INSERT INTO dbo.PtpChase
                (ChaseId, PtpId, CycleId, DealerUrn, OwnerTsi, CommitmentDate, DueDate,
                 ChaseType, Status, CorrelationId, CreatedUtc)
            VALUES
                (@ChaseId, @PtpId, @CycleId, @DealerUrn, @OwnerTsi, @CommitmentDate, @DueDate,
                 @ChaseType, @Status, @CorrelationId, @CreatedUtc);
            """;
        await using var connection = new SqlConnection(connectionString);
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            chase.ChaseId,
            chase.PtpId,
            chase.CycleId,
            chase.DealerUrn,
            chase.OwnerTsi,
            CommitmentDate = chase.CommitmentDate.ToDateTime(TimeOnly.MinValue),
            DueDate = chase.DueDate.ToDateTime(TimeOnly.MinValue),
            chase.ChaseType,
            chase.Status,
            chase.CorrelationId,
            chase.CreatedUtc
        }, cancellationToken: ct));
        return affected > 0;
    }

    public static async Task UpsertRecoveryIndexInlineAsync(
        string connectionString, RecoveryCaseIndex index, CancellationToken ct)
    {
        const string sql = """
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
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            CycleId = index.CycleId.Value,
            DealerUrn = index.DealerUrn.Value,
            index.Status,
            index.CorrelationId,
            index.WaitingGate,
            index.UpdatedUtc,
            index.RecoverabilityScore,
            index.RecoveryTier
        }, cancellationToken: ct));
    }

    public static async Task<IReadOnlyList<RecoveryCaseIndex>> ListByCycleInlineAsync(
        string connectionString, CycleId cycleId, string? region, string? depot, CancellationToken ct)
    {
        const string sql = """
            SELECT i.CycleId, i.DealerUrn, i.Status, i.CorrelationId, i.WaitingGate, i.UpdatedUtc, i.RecoverabilityScore, i.RecoveryTier
            FROM dbo.RecoveryCaseIndex i
            INNER JOIN dbo.Dealer d ON d.Urn = i.DealerUrn
            WHERE i.CycleId = @CycleId
              AND (@Region IS NULL OR d.Region = @Region)
              AND (@Depot IS NULL OR d.Depot = @Depot)
            ORDER BY i.UpdatedUtc DESC
            """;
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<IndexRow>(new CommandDefinition(sql, new
        {
            CycleId = cycleId.Value,
            Region = string.IsNullOrWhiteSpace(region) ? null : region,
            Depot = string.IsNullOrWhiteSpace(depot) ? null : depot
        }, cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public static async Task<RankedWorklist> ListRankedWorklistInlineAsync(
        string connectionString, CycleId cycleId, string? region, string? depot, bool topDecile, CancellationToken ct)
    {
        const string sql = """
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
            ORDER BY RankNo
            """;
        await using var connection = new SqlConnection(connectionString);
        var rows = (await connection.QueryAsync<WorklistRow>(new CommandDefinition(sql, new
        {
            CycleId = cycleId.Value,
            Region = string.IsNullOrWhiteSpace(region) ? null : region,
            Depot = string.IsNullOrWhiteSpace(depot) ? null : depot,
            TopDecile = topDecile ? 1 : 0
        }, cancellationToken: ct))).ToList();

        var eligibleCount = rows.Count == 0 ? 0 : rows[0].EligibleCount;
        var entries = rows.Select(r => new RankedWorklistEntry(
            (int)r.RankNo,
            new DealerUrn(r.DealerUrn),
            r.RecoverabilityScore,
            r.RecoveryTier,
            r.Status,
            r.WaitingGate)).ToList();
        return new RankedWorklist(cycleId, topDecile, eligibleCount, entries);
    }

    private sealed class IndexRow
    {
        public string CycleId { get; set; } = "";
        public string DealerUrn { get; set; } = "";
        public string Status { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public string? WaitingGate { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public decimal? RecoverabilityScore { get; set; }
        public string? RecoveryTier { get; set; }

        public RecoveryCaseIndex ToDomain() => new(
            new CycleId(CycleId),
            new DealerUrn(DealerUrn),
            Status,
            CorrelationId,
            WaitingGate,
            UpdatedUtc,
            RecoverabilityScore,
            RecoveryTier);
    }

    private sealed class WorklistRow
    {
        public string DealerUrn { get; set; } = "";
        public decimal RecoverabilityScore { get; set; }
        public string RecoveryTier { get; set; } = "";
        public string Status { get; set; } = "";
        public string? WaitingGate { get; set; }
        public long RankNo { get; set; }
        public int EligibleCount { get; set; }
    }
}
