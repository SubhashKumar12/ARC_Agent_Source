using Dapper;
using Microsoft.Data.SqlClient;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;

namespace ARC.Integration.Tests.Field;

/// <summary>
/// Reference inline SQL copied from pre-11Q repositories for parity comparison only.
/// Not used in production code paths.
/// </summary>
internal static class Batch1InlineSqlReference
{
    public static async Task<RecoveryCaseIndex?> GetRecoveryCaseIndexAsync(
        string connectionString,
        CycleId cycleId,
        DealerUrn dealerUrn,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CycleId, DealerUrn, Status, CorrelationId, WaitingGate, UpdatedUtc, RecoverabilityScore, RecoveryTier
            FROM dbo.RecoveryCaseIndex
            WHERE CycleId = @CycleId AND DealerUrn = @DealerUrn
            """;
        await using var connection = new SqlConnection(connectionString);
        var row = await connection.QuerySingleOrDefaultAsync<RecoveryIndexRow>(
            new CommandDefinition(sql, new { CycleId = cycleId.Value, DealerUrn = dealerUrn.Value }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public static async Task<DealerSourceMapping?> GetDealerSourceMappingAsync(
        string connectionString,
        string sourceSystem,
        string sourceIdentifier,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT SourceSystem, SourceIdentifier, CanonicalUrn, MatchKind
            FROM dbo.DealerSourceIdentifier
            WHERE SourceSystem = @SourceSystem AND SourceIdentifier = @SourceIdentifier
            """;
        await using var connection = new SqlConnection(connectionString);
        var row = await connection.QuerySingleOrDefaultAsync<MappingRow>(
            new CommandDefinition(
                sql,
                new
                {
                    SourceSystem = DealerSourceSystems.Normalize(sourceSystem),
                    SourceIdentifier = DealerIdentityNormalization.Identifier(sourceIdentifier)
                },
                cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public static async Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByIdentifierAsync(
        string connectionString,
        string sourceSystem,
        string sourceIdentifier,
        CancellationToken cancellationToken)
    {
        const string sql = """
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
              AND UPPER(LTRIM(RTRIM(AppId))) = @SourceIdentifier
            """;
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<string>(
            new CommandDefinition(
                sql,
                new
                {
                    SourceSystem = DealerSourceSystems.Normalize(sourceSystem),
                    SourceIdentifier = DealerIdentityNormalization.Identifier(sourceIdentifier)
                },
                cancellationToken: cancellationToken));
        return rows.Select(u => new DealerUrn(u)).ToList();
    }

    public static async Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByAliasAsync(
        string connectionString,
        string normalizedAlias,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT CanonicalUrn
            FROM dbo.DealerAlias
            WHERE AliasValue = @AliasValue
            """;
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<string>(
            new CommandDefinition(
                sql,
                new { AliasValue = DealerIdentityNormalization.Alias(normalizedAlias) },
                cancellationToken: cancellationToken));
        return rows.Select(u => new DealerUrn(u)).ToList();
    }

    public static async Task<IReadOnlyList<GateDecision>> ListGateDecisionsAsync(
        string connectionString,
        CycleId cycleId,
        DealerUrn dealerUrn,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT GateId, ActorUpn, ActorRole, Decision, Reason, RecommendedAction, DecidedUtc, CorrelationId
            FROM dbo.GateDecision
            WHERE CycleId = @CycleId AND DealerUrn = @DealerUrn
            ORDER BY DecidedUtc
            """;
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<GateRow>(
            new CommandDefinition(sql, new { CycleId = cycleId.Value, DealerUrn = dealerUrn.Value }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed class RecoveryIndexRow
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

    private sealed class MappingRow
    {
        public string SourceSystem { get; set; } = "";
        public string SourceIdentifier { get; set; } = "";
        public string CanonicalUrn { get; set; } = "";
        public string MatchKind { get; set; } = "";

        public DealerSourceMapping ToDomain() => new(
            SourceSystem,
            SourceIdentifier,
            new DealerUrn(CanonicalUrn),
            Enum.Parse<DealerMatchKind>(MatchKind, ignoreCase: true));
    }

    private sealed class GateRow
    {
        public string GateId { get; set; } = "";
        public string ActorUpn { get; set; } = "";
        public string ActorRole { get; set; } = "";
        public string Decision { get; set; } = "";
        public string Reason { get; set; } = "";
        public string? RecommendedAction { get; set; }
        public DateTimeOffset DecidedUtc { get; set; }
        public string CorrelationId { get; set; } = "";

        public GateDecision ToDomain() => GateDecision.Create(
            Enum.Parse<GateId>(GateId, ignoreCase: true),
            ActorUpn,
            Enum.Parse<ActorRole>(ActorRole, ignoreCase: true),
            Enum.Parse<GateDecisionStatus>(Decision, ignoreCase: true),
            Reason,
            new CorrelationId(CorrelationId),
            RecommendedAction,
            DecidedUtc);
    }
}
