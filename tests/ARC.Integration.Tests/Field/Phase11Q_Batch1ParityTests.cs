using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;
using ARC.Integration.Tests.Fixtures;

namespace ARC.Integration.Tests.Field;

[Trait("Category", "Integration")]
[Collection("Sql-Schema")]
public sealed class Phase11Q_Batch1ParityTests : IAsyncLifetime
{
    private readonly SqlFixture _sql = new();
    private const string DealerUrn = "dealer:11q-batch1";
    private const string Cycle = "2026-03-11q";
    private const string PlanId = "2026-03-11q|tsi.west@paintco.local";
    private const string RecordId = "2026-03-11q|dealer:11q-batch1|ptp|20260401";
    private const string ChaseId = "2026-03-11q|dealer:11q-batch1|ptp|20260401|broken";

    public async Task InitializeAsync()
    {
        await InfrastructureGate.EnsureSqlAvailableAsync(CancellationToken.None);
        await _sql.InitializeAsync(CancellationToken.None);

        var count = await ArcOwnedProcedureDeployer.CountDeployedProceduresAsync(_sql.ConnectionString, CancellationToken.None);
        Assert.Equal(23, count);
        Assert.True(await ArcOwnedProcedureDeployer.VisitPlanLineInputTypeExistsAsync(_sql.ConnectionString, CancellationToken.None));

        await SeedAsync();
    }

    public Task DisposeAsync() => CleanupAsync();

    [Fact]
    public async Task RecoveryCase_GetAsync_matches_inline_reference()
    {
        await using var provider = CreateScope();
        var repo = provider.GetRequiredService<IRecoveryCaseRepository>();
        var cycleId = new CycleId(Cycle);
        var urn = new DealerUrn(DealerUrn);

        var fromSp = await repo.GetAsync(cycleId, urn, CancellationToken.None);
        var fromInline = await Batch1InlineSqlReference.GetRecoveryCaseIndexAsync(
            _sql.ConnectionString, cycleId, urn, CancellationToken.None);

        Assert.NotNull(fromSp);
        Assert.NotNull(fromInline);
        Assert.Equal(fromInline!.Status, fromSp!.Status);
        Assert.Equal(fromInline.CorrelationId, fromSp.CorrelationId);
        Assert.Equal(fromInline.WaitingGate, fromSp.WaitingGate);
        Assert.Equal(fromInline.RecoverabilityScore, fromSp.RecoverabilityScore);
        Assert.Equal(fromInline.RecoveryTier, fromSp.RecoveryTier);
    }

    [Fact]
    public async Task Identity_GetMapping_matches_inline_reference()
    {
        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IDealerIdentityMappingRepository>();

        var fromSp = await repo.GetMappingAsync(DealerSourceSystems.Sap, "SAP-11Q", CancellationToken.None);
        var fromInline = await Batch1InlineSqlReference.GetDealerSourceMappingAsync(
            _sql.ConnectionString, DealerSourceSystems.Sap, "SAP-11Q", CancellationToken.None);

        Assert.NotNull(fromSp);
        Assert.NotNull(fromInline);
        Assert.Equal(fromInline!.CanonicalUrn.Value, fromSp!.CanonicalUrn.Value);
        Assert.Equal(fromInline.MatchKind, fromSp.MatchKind);
    }

    [Fact]
    public async Task Identity_FindByIdentifier_matches_inline_reference()
    {
        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IDealerIdentityMappingRepository>();

        var fromSp = await repo.FindDealerUrnsByIdentifierAsync(DealerSourceSystems.Portal, "PORTAL-11Q", CancellationToken.None);
        var fromInline = await Batch1InlineSqlReference.FindDealerUrnsByIdentifierAsync(
            _sql.ConnectionString, DealerSourceSystems.Portal, "PORTAL-11Q", CancellationToken.None);

        Assert.Equal(fromInline.Select(u => u.Value).OrderBy(x => x), fromSp.Select(u => u.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task Identity_FindByAlias_matches_inline_reference()
    {
        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IDealerIdentityMappingRepository>();

        var fromSp = await repo.FindDealerUrnsByAliasValueAsync("M/S 11Q HARDWARE", CancellationToken.None);
        var fromInline = await Batch1InlineSqlReference.FindDealerUrnsByAliasAsync(
            _sql.ConnectionString, "M/S 11Q HARDWARE", CancellationToken.None);

        Assert.Equal(fromInline.Select(u => u.Value).OrderBy(x => x), fromSp.Select(u => u.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task GateDecision_ListAsync_omits_WasOverride_and_matches_inline_reference()
    {
        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IGateDecisionRepository>();
        var cycleId = new CycleId(Cycle);
        var urn = new DealerUrn(DealerUrn);

        var fromSp = await repo.ListAsync(cycleId, urn, CancellationToken.None);
        var fromInline = await Batch1InlineSqlReference.ListGateDecisionsAsync(
            _sql.ConnectionString, cycleId, urn, CancellationToken.None);

        Assert.Equal(fromInline.Count, fromSp.Count);
        for (var i = 0; i < fromInline.Count; i++)
        {
            Assert.Equal(fromInline[i].Gate, fromSp[i].Gate);
            Assert.Equal(fromInline[i].Decision, fromSp[i].Decision);
            Assert.Equal(fromInline[i].Reason, fromSp[i].Reason);
            Assert.Equal(fromInline[i].CorrelationId.Value, fromSp[i].CorrelationId.Value);
        }

        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT WasOverride FROM dbo.GateDecision WHERE CycleId = @CycleId AND DealerUrn = @DealerUrn";
        cmd.Parameters.AddWithValue("@CycleId", Cycle);
        cmd.Parameters.AddWithValue("@DealerUrn", DealerUrn);
        Assert.True(Convert.ToBoolean(await cmd.ExecuteScalarAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task PtpRecord_GetAsync_survives_repository_restart()
    {
        PtpRecordEntity? first;
        await using (var scopeA = CreateScope())
        {
            first = await scopeA.GetRequiredService<IPtpRecordRepository>()
                .GetAsync(RecordId, CancellationToken.None);
        }

        await using var scopeB = CreateScope();
        var second = await scopeB.GetRequiredService<IPtpRecordRepository>()
            .GetAsync(RecordId, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Status, second!.Status);
        Assert.Equal(first.Amount, second.Amount);
    }

    [Fact]
    public async Task VisitPlan_GetAsync_header_lines_and_sequence_match()
    {
        await using var scope = CreateScope();
        var plan = await scope.GetRequiredService<IVisitPlanRepository>()
            .GetAsync(PlanId, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal(Cycle, plan!.CycleId);
        Assert.Equal(2, plan.Lines.Count);
        Assert.Equal([1, 2], plan.Lines.Select(l => l.Sequence).ToArray());
        Assert.Equal("dealer:11q-a", plan.Lines[0].DealerUrn);
        Assert.Equal("dealer:11q-b", plan.Lines[1].DealerUrn);
    }

    [Fact]
    public async Task PtpChase_GetAsync_returns_seeded_row()
    {
        await using var scope = CreateScope();
        var chase = await scope.GetRequiredService<IPtpChaseRepository>()
            .GetAsync(ChaseId, CancellationToken.None);

        Assert.NotNull(chase);
        Assert.Equal("Broken", chase!.ChaseType);
        Assert.Equal("Open", chase.Status);
    }

    private ServiceProvider CreateScope()
    {
        var services = new ServiceCollection();
        services.AddIntegrationSqlRepositories(_sql.ConnectionString);
        return services.BuildServiceProvider();
    }

    private async Task SeedAsync()
    {
        await CleanupAsync();
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();

        await Exec(connection, """
            INSERT INTO dbo.Dealer (Urn, SapCode, PortalId, AppId, Depot, Region, CoveringTsi, UnderInsolvencyMoratorium)
            VALUES (@Urn, N'SAP-11Q', N'PORTAL-11Q', N'APP-11Q', N'Mumbai-Andheri', N'West', N'tsi.west@paintco.local', 0);
            """, ("@Urn", DealerUrn));

        await Exec(connection, """
            INSERT INTO dbo.DealerSourceIdentifier (SourceSystem, SourceIdentifier, CanonicalUrn, MatchKind, UpdatedUtc)
            VALUES (N'SAP', N'SAP-11Q', @Urn, N'ExistingMapping', SYSUTCDATETIME());
            """, ("@Urn", DealerUrn));

        await Exec(connection, """
            INSERT INTO dbo.DealerAlias (CanonicalUrn, AliasKind, AliasValue)
            VALUES (@Urn, N'TradeLegalName', N'M/S 11Q HARDWARE');
            """, ("@Urn", DealerUrn));

        await Exec(connection, """
            INSERT INTO dbo.RecoveryCaseIndex
                (CycleId, DealerUrn, Status, CorrelationId, WaitingGate, UpdatedUtc, RecoverabilityScore, RecoveryTier)
            VALUES (@Cycle, @Urn, N'Active', N'corr-11q', N'DepotManager', SYSUTCDATETIME(), 88.5, N'Tier1');
            """, ("@Cycle", Cycle), ("@Urn", DealerUrn));

        await Exec(connection, """
            INSERT INTO dbo.GateDecision
                (CycleId, DealerUrn, GateId, ActorUpn, ActorRole, Decision, Reason, RecommendedAction, DecidedUtc, CorrelationId, WasOverride)
            VALUES (@Cycle, @Urn, N'DepotManager', N'dm@paintco.local', N'DepotManager', N'Approved', N'ok', NULL, SYSUTCDATETIME(), N'gate-11q', 1);
            """, ("@Cycle", Cycle), ("@Urn", DealerUrn));

        await Exec(connection, """
            INSERT INTO dbo.PtpRecord
                (RecordId, CycleId, DealerUrn, CommitmentDate, Amount, Currency, Status, ConfirmedByTsi,
                 RequiresTsiConfirmation, Discarded, Locale, CorrelationId, CreatedUtc, UpdatedUtc)
            VALUES (@RecordId, @Cycle, @Urn, '2026-04-01', 9000, N'INR', N'Captured', 0, 1, 0, N'en-IN', N'corr-ptp', SYSUTCDATETIME(), SYSUTCDATETIME());
            """, ("@RecordId", RecordId), ("@Cycle", Cycle), ("@Urn", DealerUrn));

        await Exec(connection, """
            INSERT INTO dbo.VisitPlan (PlanId, CycleId, TsiId, PlanDate, CorrelationId, CreatedUtc, UpdatedUtc)
            VALUES (@PlanId, @Cycle, N'tsi.west@paintco.local', '2026-04-02', N'corr-plan', SYSUTCDATETIME(), SYSUTCDATETIME());
            """, ("@PlanId", PlanId), ("@Cycle", Cycle));

        await Exec(connection, """
            INSERT INTO dbo.VisitPlanLine (PlanId, DealerUrn, Sequence, PriorityRank, GeoClusterId, Reason, Status, VisitTaskId, CreatedUtc)
            VALUES
                (@PlanId, N'dealer:11q-a', 1, 1, N'cluster-a', N'PostNotice', N'Planned', N'task-a', SYSUTCDATETIME()),
                (@PlanId, N'dealer:11q-b', 2, 2, N'cluster-b', N'PostNotice', N'Planned', N'task-b', SYSUTCDATETIME());
            """, ("@PlanId", PlanId));

        await Exec(connection, """
            INSERT INTO dbo.PtpChase
                (ChaseId, PtpId, CycleId, DealerUrn, OwnerTsi, CommitmentDate, DueDate, ChaseType, Status, CorrelationId, CreatedUtc)
            VALUES (@ChaseId, @RecordId, @Cycle, @Urn, N'tsi.west@paintco.local', '2026-04-01', '2026-04-02', N'Broken', N'Open', N'corr-chase', SYSUTCDATETIME());
            """, ("@ChaseId", ChaseId), ("@RecordId", RecordId), ("@Cycle", Cycle), ("@Urn", DealerUrn));
    }

    private async Task CleanupAsync()
    {
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await Exec(connection, """
            DELETE FROM dbo.PtpChase WHERE CycleId = @Cycle OR DealerUrn = @Urn;
            DELETE FROM dbo.VisitPlanLine WHERE PlanId = @PlanId;
            DELETE FROM dbo.VisitPlan WHERE PlanId = @PlanId OR CycleId = @Cycle;
            DELETE FROM dbo.PtpRecord WHERE RecordId = @RecordId OR CycleId = @Cycle;
            DELETE FROM dbo.GateDecision WHERE CycleId = @Cycle OR DealerUrn = @Urn;
            DELETE FROM dbo.RecoveryCaseIndex WHERE CycleId = @Cycle OR DealerUrn = @Urn;
            DELETE FROM dbo.DealerAlias WHERE CanonicalUrn = @Urn;
            DELETE FROM dbo.DealerSourceIdentifier WHERE CanonicalUrn = @Urn;
            DELETE FROM dbo.Dealer WHERE Urn = @Urn;
            """,
            ("@Cycle", Cycle), ("@Urn", DealerUrn), ("@PlanId", PlanId), ("@RecordId", RecordId));
    }

    private static async Task Exec(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
