using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;
using ARC.Integration.Tests.Fixtures;

namespace ARC.Integration.Tests.Field;

[Trait("Category", "Integration")]
[Collection("Sql-Schema")]
public sealed class Phase11RS_ParityTests : IAsyncLifetime
{
    private readonly SqlFixture _sql = new();
    private const string Cycle = "2026-04-11rs";
    private const string DealerA = "dealer:11rs-a";
    private const string DealerB = "dealer:11rs-b";
    private const string PlanId = "2026-04-11rs|tsi.west@paintco.local";
    private const string RecordId = "2026-04-11rs|dealer:11rs-a|ptp|20260415";
    private const string ChaseNewId = "2026-04-11rs|dealer:11rs-a|ptp|20260415|reminder";

    public async Task InitializeAsync()
    {
        await InfrastructureGate.EnsureSqlAvailableAsync(CancellationToken.None);
        await _sql.InitializeAsync(CancellationToken.None);
        await SeedAsync();
    }

    public Task DisposeAsync() => CleanupAsync();

    // ===== Batch A =====

    [Fact]
    public async Task BatchA_GateDecision_Save_is_idempotent_and_persists_WasOverride()
    {
        var cycleId = new CycleId(Cycle);
        var urn = new DealerUrn(DealerA);
        var decision = GateDecision.Create(
            GateId.DepotManager,
            "dm@paintco.local",
            ActorRole.DepotManager,
            GateDecisionStatus.Declined,
            "batch-a override",
            new CorrelationId("gate-11rs-a"),
            recommendedAction: "Approve",
            decidedUtc: DateTimeOffset.UtcNow);

        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IGateDecisionRepository>();
        await repo.SaveAsync(cycleId, urn, decision, CancellationToken.None);
        await repo.SaveAsync(cycleId, urn, decision, CancellationToken.None);

        var count = await Phase11RS_InlineSqlReference.CountGateDecisionsAsync(
            _sql.ConnectionString, cycleId, urn, "gate-11rs-a", CancellationToken.None);
        Assert.Equal(1, count);

        var listed = await repo.ListAsync(cycleId, urn, CancellationToken.None);
        Assert.Single(listed);
        Assert.Equal(GateDecisionStatus.Declined, listed[0].Decision);

        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT WasOverride FROM dbo.GateDecision WHERE CycleId = @CycleId AND CorrelationId = @CorrelationId";
        cmd.Parameters.AddWithValue("@CycleId", Cycle);
        cmd.Parameters.AddWithValue("@CorrelationId", "gate-11rs-a");
        Assert.True(Convert.ToBoolean(await cmd.ExecuteScalarAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task BatchA_SaveAlias_is_idempotent()
    {
        var urn = new DealerUrn(DealerA);
        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IDealerIdentityMappingRepository>();
        await repo.SaveAliasAsync(urn, "TradeLegalName", "M/S 11RS NEW ALIAS", CancellationToken.None);
        await repo.SaveAliasAsync(urn, "TradeLegalName", "M/S 11RS NEW ALIAS", CancellationToken.None);

        var found = await repo.FindDealerUrnsByAliasValueAsync("M/S 11RS NEW ALIAS", CancellationToken.None);
        Assert.Single(found);
        Assert.Equal(DealerA, found[0].Value);
    }

    [Fact]
    public async Task BatchA_SaveResolvedMapping_inserts_and_updates_match_kind()
    {
        var mapping = new DealerSourceMapping(
            DealerSourceSystems.Portal,
            "PORTAL-11RS-NEW",
            new DealerUrn(DealerA),
            DealerMatchKind.ExistingMapping);

        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IDealerIdentityMappingRepository>();
        await repo.SaveResolvedMappingAsync(mapping, CancellationToken.None);

        var loaded = await repo.GetMappingAsync(DealerSourceSystems.Portal, "PORTAL-11RS-NEW", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(DealerA, loaded!.CanonicalUrn.Value);

        var updated = mapping with { MatchKind = DealerMatchKind.ExactIdentifier };
        await repo.SaveResolvedMappingAsync(updated, CancellationToken.None);
        loaded = await repo.GetMappingAsync(DealerSourceSystems.Portal, "PORTAL-11RS-NEW", CancellationToken.None);
        Assert.Equal(DealerMatchKind.ExactIdentifier, loaded!.MatchKind);
    }

    [Fact]
    public async Task BatchA_SaveResolvedMapping_conflict_throws_DuplicatePersistenceException()
    {
        var mapping = new DealerSourceMapping(
            DealerSourceSystems.Sap,
            "SAP-CONFLICT",
            new DealerUrn(DealerB),
            DealerMatchKind.ExistingMapping);

        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await Exec(connection,
            "INSERT INTO dbo.DealerSourceIdentifier (SourceSystem, SourceIdentifier, CanonicalUrn, MatchKind, UpdatedUtc) VALUES (N'SAP', N'SAP-CONFLICT', @Urn, N'ExistingMapping', SYSUTCDATETIME());",
            ("@Urn", DealerA));

        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IDealerIdentityMappingRepository>();
        await Assert.ThrowsAsync<DuplicatePersistenceException>(() =>
            repo.SaveResolvedMappingAsync(mapping, CancellationToken.None));
    }

    [Fact]
    public async Task BatchA_PtpChase_TryInsert_insert_if_absent_and_idempotent()
    {
        var chase = new PtpChaseEntity
        {
            ChaseId = ChaseNewId,
            PtpId = RecordId,
            CycleId = Cycle,
            DealerUrn = DealerA,
            OwnerTsi = "tsi.west@paintco.local",
            CommitmentDate = new DateOnly(2026, 4, 15),
            DueDate = new DateOnly(2026, 4, 16),
            ChaseType = "Reminder",
            Status = "Open",
            CorrelationId = "corr-chase-new",
            CreatedUtc = DateTimeOffset.UtcNow
        };

        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IPtpChaseRepository>();
        Assert.True(await repo.TryInsertAsync(chase, CancellationToken.None));
        Assert.False(await repo.TryInsertAsync(chase, CancellationToken.None));

        var loaded = await repo.GetAsync(ChaseNewId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("Reminder", loaded!.ChaseType);
    }

    // ===== Batch B =====

    [Fact]
    public async Task BatchB_RecoveryCase_Upsert_coalesce_score_tier_and_survives_restart()
    {
        var cycleId = new CycleId(Cycle);
        var urn = new DealerUrn(DealerA);
        var index = new RecoveryCaseIndex(
            cycleId, urn, "Active", "corr-upsert", "DepotManager",
            DateTimeOffset.UtcNow, 75.0m, "Tier2");

        await using (var scope = CreateScope())
        {
            await scope.GetRequiredService<IRecoveryCaseRepository>()
                .UpsertIndexAsync(index, CancellationToken.None);
        }

        var partial = index with { RecoverabilityScore = null, RecoveryTier = null, Status = "Waiting" };
        await using (var scope = CreateScope())
        {
            await scope.GetRequiredService<IRecoveryCaseRepository>()
                .UpsertIndexAsync(partial, CancellationToken.None);
        }

        await using var readScope = CreateScope();
        var loaded = await readScope.GetRequiredService<IRecoveryCaseRepository>()
            .GetAsync(cycleId, urn, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("Waiting", loaded!.Status);
        Assert.Equal(75.0m, loaded.RecoverabilityScore);
        Assert.Equal("Tier2", loaded.RecoveryTier);
    }

    [Fact]
    public async Task BatchB_PtpRecord_Upsert_read_restart_parity()
    {
        var record = new PtpRecordEntity
        {
            RecordId = "2026-04-11rs|dealer:11rs-b|ptp|20260420",
            CycleId = Cycle,
            DealerUrn = DealerB,
            CommitmentDate = new DateOnly(2026, 4, 20),
            Amount = 12000m,
            Currency = "INR",
            Status = "Confirmed",
            ConfirmedByTsi = true,
            ConfirmedUtc = DateTimeOffset.UtcNow,
            ConfirmedByUpn = "tsi.west@paintco.local",
            RequiresTsiConfirmation = false,
            Discarded = false,
            Locale = "en-IN",
            CorrelationId = "corr-ptp-b",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await using (var scope = CreateScope())
            await scope.GetRequiredService<IPtpRecordRepository>().UpsertAsync(record, CancellationToken.None);

        await using var readScope = CreateScope();
        var loaded = await readScope.GetRequiredService<IPtpRecordRepository>()
            .GetAsync(record.RecordId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("Confirmed", loaded!.Status);
        Assert.Equal(12000m, loaded.Amount);
    }

    [Fact]
    public async Task BatchB_VisitPlan_Upsert_replaces_lines_in_sequence()
    {
        var plan = new VisitPlanEntity
        {
            PlanId = "2026-04-11rs|tsi.east@paintco.local",
            CycleId = Cycle,
            TsiId = "tsi.east@paintco.local",
            PlanDate = new DateOnly(2026, 4, 22),
            CorrelationId = "corr-plan-b",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Lines =
            [
                new VisitPlanLineEntity
                {
                    PlanId = "2026-04-11rs|tsi.east@paintco.local",
                    DealerUrn = DealerB,
                    Sequence = 1,
                    PriorityRank = 1,
                    GeoClusterId = "c1",
                    Reason = "FollowUp",
                    Status = "Planned",
                    VisitTaskId = "task-1",
                    CreatedUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        await using (var scope = CreateScope())
            await scope.GetRequiredService<IVisitPlanRepository>().UpsertAsync(plan, CancellationToken.None);

        plan = new VisitPlanEntity
        {
            PlanId = plan.PlanId,
            CycleId = plan.CycleId,
            TsiId = plan.TsiId,
            PlanDate = plan.PlanDate,
            CorrelationId = plan.CorrelationId,
            CreatedUtc = plan.CreatedUtc,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Lines =
            [
                new VisitPlanLineEntity
                {
                    PlanId = plan.PlanId,
                    DealerUrn = DealerA,
                    Sequence = 1,
                    PriorityRank = 1,
                    GeoClusterId = "c1",
                    Reason = "FollowUp",
                    Status = "Planned",
                    VisitTaskId = "task-1",
                    CreatedUtc = DateTimeOffset.UtcNow
                },
                new VisitPlanLineEntity
                {
                    PlanId = plan.PlanId,
                    DealerUrn = DealerB,
                    Sequence = 2,
                    PriorityRank = 2,
                    GeoClusterId = "c2",
                    Reason = "FollowUp",
                    Status = "Planned",
                    VisitTaskId = "task-2",
                    CreatedUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        await using (var scope = CreateScope())
            await scope.GetRequiredService<IVisitPlanRepository>().UpsertAsync(plan, CancellationToken.None);

        await using var readScope = CreateScope();
        var loaded = await readScope.GetRequiredService<IVisitPlanRepository>()
            .GetAsync(plan.PlanId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Lines.Count);
        Assert.Equal([1, 2], loaded.Lines.Select(l => l.Sequence).ToArray());
    }

    // ===== Batch C =====

    [Fact]
    public async Task BatchC_RecoveryCase_ListByCycle_matches_inline_reference()
    {
        var cycleId = new CycleId(Cycle);
        await using var scope = CreateScope();
        var fromSp = await scope.GetRequiredService<IRecoveryCaseRepository>()
            .ListByCycleAsync(cycleId, "West", null, CancellationToken.None);
        var fromInline = await Phase11RS_InlineSqlReference.ListByCycleInlineAsync(
            _sql.ConnectionString, cycleId, "West", null, CancellationToken.None);

        Assert.Equal(fromInline.Count, fromSp.Count);
        Assert.Equal(
            fromInline.Select(i => i.DealerUrn.Value).OrderBy(x => x),
            fromSp.Select(i => i.DealerUrn.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task BatchC_RecoveryCase_ListRankedWorklist_matches_inline_reference()
    {
        var cycleId = new CycleId(Cycle);
        await using var scope = CreateScope();
        var fromSp = await scope.GetRequiredService<IRecoveryCaseRepository>()
            .ListRankedWorklistAsync(cycleId, "West", null, topDecile: true, CancellationToken.None);
        var fromInline = await Phase11RS_InlineSqlReference.ListRankedWorklistInlineAsync(
            _sql.ConnectionString, cycleId, "West", null, topDecile: true, CancellationToken.None);

        Assert.Equal(fromInline.EligibleCount, fromSp.EligibleCount);
        Assert.Equal(fromInline.Entries.Count, fromSp.Entries.Count);
        for (var i = 0; i < fromInline.Entries.Count; i++)
        {
            Assert.Equal(fromInline.Entries[i].Rank, fromSp.Entries[i].Rank);
            Assert.Equal(fromInline.Entries[i].DealerUrn.Value, fromSp.Entries[i].DealerUrn.Value);
        }
    }

    [Fact]
    public async Task BatchC_PtpRecord_list_operations_match_filters()
    {
        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IPtpRecordRepository>();

        var byCycle = await repo.ListCommittedByCycleAsync(Cycle, CancellationToken.None);
        Assert.NotEmpty(byCycle);
        Assert.All(byCycle, r => Assert.Equal(Cycle, r.CycleId));

        var byDealer = await repo.ListCommittedByDealerAsync(DealerA, CancellationToken.None);
        Assert.NotEmpty(byDealer);
        Assert.All(byDealer, r => Assert.Equal(DealerA, r.DealerUrn));

        var due = await repo.ListDueConfirmedAsync(new DateOnly(2026, 4, 30), CancellationToken.None);
        Assert.DoesNotContain(due, r => r.RecordId == RecordId);

        const string dueRecordId = "2026-04-11rs|dealer:11rs-b|ptp|20260301";
        await using (var connection = new SqlConnection(_sql.ConnectionString))
        {
            await connection.OpenAsync();
            await Exec(connection, """
                INSERT INTO dbo.PtpRecord
                    (RecordId, CycleId, DealerUrn, CommitmentDate, Amount, Currency, Status, ConfirmedByTsi,
                     ConfirmedUtc, ConfirmedByUpn, RequiresTsiConfirmation, Discarded, Locale, CorrelationId, CreatedUtc, UpdatedUtc)
                VALUES (@RecordId, @Cycle, @Urn, '2026-03-01', 5000, N'INR', N'Confirmed', 1,
                        SYSUTCDATETIME(), N'tsi.west@paintco.local', 0, 0, N'en-IN', N'corr-due', SYSUTCDATETIME(), SYSUTCDATETIME());
                """, ("@RecordId", dueRecordId), ("@Cycle", Cycle), ("@Urn", DealerB));
        }

        due = await repo.ListDueConfirmedAsync(new DateOnly(2026, 4, 30), CancellationToken.None);
        Assert.Contains(due, r => r.RecordId == dueRecordId);
    }

    [Fact]
    public async Task BatchC_VisitPlan_ListByCycle_returns_headers_and_lines()
    {
        await using var scope = CreateScope();
        var plans = await scope.GetRequiredService<IVisitPlanRepository>()
            .ListByCycleAsync(Cycle, CancellationToken.None);

        Assert.NotEmpty(plans);
        Assert.Contains(plans, p => p.PlanId == PlanId && p.Lines.Count == 2);
    }

    [Fact]
    public async Task BatchC_PtpChase_list_operations_match_inline_filters()
    {
        await using var scope = CreateScope();
        var repo = scope.GetRequiredService<IPtpChaseRepository>();

        var byCycle = await repo.ListByCycleAsync(Cycle, CancellationToken.None);
        Assert.NotEmpty(byCycle);
        Assert.All(byCycle, c => Assert.Equal(Cycle, c.CycleId));

        var byStatus = await repo.ListByStatusAsync("Open", CancellationToken.None);
        Assert.Contains(byStatus, c => c.ChaseId.Contains("11rs"));
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

        foreach (var (urn, sap, portal, score) in new[]
        {
            (DealerA, "SAP-11RS-A", "PORTAL-11RS-A", 90.0m),
            (DealerB, "SAP-11RS-B", "PORTAL-11RS-B", 80.0m)
        })
        {
            await Exec(connection, """
                INSERT INTO dbo.Dealer (Urn, SapCode, PortalId, AppId, Depot, Region, CoveringTsi, UnderInsolvencyMoratorium)
                VALUES (@Urn, @Sap, @Portal, N'APP', N'Mumbai-Andheri', N'West', N'tsi.west@paintco.local', 0);
                INSERT INTO dbo.RecoveryCaseIndex
                    (CycleId, DealerUrn, Status, CorrelationId, WaitingGate, UpdatedUtc, RecoverabilityScore, RecoveryTier)
                VALUES (@Cycle, @Urn, N'Active', N'corr-seed', N'DepotManager', SYSUTCDATETIME(), @Score, N'Tier1');
                """,
                ("@Urn", urn), ("@Sap", sap), ("@Portal", portal), ("@Cycle", Cycle), ("@Score", score));
        }

        await Exec(connection, """
            INSERT INTO dbo.PtpRecord
                (RecordId, CycleId, DealerUrn, CommitmentDate, Amount, Currency, Status, ConfirmedByTsi,
                 ConfirmedUtc, ConfirmedByUpn, RequiresTsiConfirmation, Discarded, Locale, CorrelationId, CreatedUtc, UpdatedUtc)
            VALUES (@RecordId, @Cycle, @Urn, '2026-04-01', 9000, N'INR', N'Confirmed', 1,
                    SYSUTCDATETIME(), N'tsi.west@paintco.local', 0, 0, N'en-IN', N'corr-ptp', SYSUTCDATETIME(), SYSUTCDATETIME());
            """, ("@RecordId", RecordId), ("@Cycle", Cycle), ("@Urn", DealerA));

        await Exec(connection, """
            INSERT INTO dbo.VisitPlan (PlanId, CycleId, TsiId, PlanDate, CorrelationId, CreatedUtc, UpdatedUtc)
            VALUES (@PlanId, @Cycle, N'tsi.west@paintco.local', '2026-04-02', N'corr-plan', SYSUTCDATETIME(), SYSUTCDATETIME());
            INSERT INTO dbo.VisitPlanLine (PlanId, DealerUrn, Sequence, PriorityRank, GeoClusterId, Reason, Status, VisitTaskId, CreatedUtc)
            VALUES
                (@PlanId, @UrnA, 1, 1, N'cluster-a', N'PostNotice', N'Planned', N'task-a', SYSUTCDATETIME()),
                (@PlanId, @UrnB, 2, 2, N'cluster-b', N'PostNotice', N'Planned', N'task-b', SYSUTCDATETIME());
            INSERT INTO dbo.PtpChase
                (ChaseId, PtpId, CycleId, DealerUrn, OwnerTsi, CommitmentDate, DueDate, ChaseType, Status, CorrelationId, CreatedUtc)
            VALUES (@ChaseId, @RecordId, @Cycle, @UrnA, N'tsi.west@paintco.local', '2026-04-01', '2026-04-02', N'Broken', N'Open', N'corr-chase', SYSUTCDATETIME());
            """,
            ("@PlanId", PlanId), ("@Cycle", Cycle), ("@UrnA", DealerA), ("@UrnB", DealerB),
            ("@ChaseId", "2026-04-11rs|dealer:11rs-a|ptp|20260401|broken"), ("@RecordId", RecordId));
    }

    private async Task CleanupAsync()
    {
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await Exec(connection, """
            DELETE FROM dbo.PtpChase WHERE CycleId = @Cycle OR DealerUrn IN (@A, @B);
            DELETE FROM dbo.VisitPlanLine WHERE PlanId LIKE @Cycle + N'%';
            DELETE FROM dbo.VisitPlan WHERE CycleId = @Cycle OR PlanId LIKE @Cycle + N'%';
            DELETE FROM dbo.PtpRecord WHERE CycleId = @Cycle OR DealerUrn IN (@A, @B);
            DELETE FROM dbo.GateDecision WHERE CycleId = @Cycle OR DealerUrn IN (@A, @B);
            DELETE FROM dbo.DealerAlias WHERE CanonicalUrn IN (@A, @B);
            DELETE FROM dbo.DealerSourceIdentifier WHERE CanonicalUrn IN (@A, @B) OR SourceIdentifier LIKE N'%11RS%';
            DELETE FROM dbo.RecoveryCaseIndex WHERE CycleId = @Cycle OR DealerUrn IN (@A, @B);
            DELETE FROM dbo.Dealer WHERE Urn IN (@A, @B);
            """, ("@Cycle", Cycle), ("@A", DealerA), ("@B", DealerB));
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
