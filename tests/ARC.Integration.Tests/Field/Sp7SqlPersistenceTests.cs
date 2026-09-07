using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Integration.Tests.Fixtures;
using ARC.Tools.Field;
using ARC.Tools.Persistence;

namespace ARC.Integration.Tests.Field;

/// <summary>
/// Phase 10D SP7 SQL durability. Uses new repository instances for restart simulation.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sql-Schema")]
public sealed class Sp7SqlPersistenceTests : IAsyncLifetime
{
    private readonly SqlFixture _sql = new();
    private const string DealerUrn = "dealer:sp7-ptp";
    private const string Cycle = "2026-03-sp7";

    public async Task InitializeAsync()
    {
        await InfrastructureGate.EnsureSqlAvailableAsync(CancellationToken.None);
        await _sql.InitializeAsync(CancellationToken.None);
        await CleanupAsync();
        await SeedDealerAsync();
    }

    public async Task DisposeAsync() => await CleanupAsync();

    [Fact]
    public async Task Candidate_persists_and_survives_new_repository_instance()
    {
        var recordId = $"{Cycle}|{DealerUrn}|ptp|20260401";
        await using (var lifeA = CreateLifetime())
        {
            var repo = lifeA.GetRequiredService<IPtpRecordRepository>();
            await repo.UpsertAsync(NewCandidate(recordId, new DateOnly(2026, 4, 1), 12_000m, "Captured", confirmed: false), CancellationToken.None);
        }

        await using var lifeB = CreateLifetime();
        var reloaded = await lifeB.GetRequiredService<IPtpRecordRepository>().GetAsync(recordId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("Captured", reloaded!.Status);
        Assert.False(reloaded.ConfirmedByTsi);
        Assert.Equal(12_000m, reloaded.Amount);
        Assert.Null(typeof(PtpRecordEntity).GetProperty("Transcript"));
        Assert.Null(typeof(PtpRecordEntity).GetProperty("RawTranscript"));
    }

    [Fact]
    public async Task Confirm_persists_tsi_fields_and_excludes_unconfirmed_from_committed()
    {
        var recordId = $"{Cycle}|{DealerUrn}|ptp|20260301";
        await using (var lifeA = CreateLifetime())
        {
            var repo = lifeA.GetRequiredService<IPtpRecordRepository>();
            await repo.UpsertAsync(NewCandidate(recordId, new DateOnly(2026, 3, 1), 5_000m, "Captured", confirmed: false), CancellationToken.None);
            var confirmedUtc = DateTimeOffset.Parse("2026-03-02T10:00:00Z");
            await repo.UpsertAsync(NewCandidate(
                recordId, new DateOnly(2026, 3, 1), 5_000m, "Confirmed", confirmed: true,
                confirmedUtc: confirmedUtc, confirmedByUpn: "tsi.west@paintco.local"), CancellationToken.None);
        }

        await using var lifeB = CreateLifetime();
        var repoB = lifeB.GetRequiredService<IPtpRecordRepository>();
        var row = await repoB.GetAsync(recordId, CancellationToken.None);
        Assert.True(row!.ConfirmedByTsi);
        Assert.Equal("Confirmed", row.Status);
        Assert.Equal("tsi.west@paintco.local", row.ConfirmedByUpn);
        Assert.NotNull(row.ConfirmedUtc);

        var committed = await repoB.ListCommittedByCycleAsync(Cycle, CancellationToken.None);
        Assert.Contains(committed, c => c.RecordId == recordId);

        await repoB.UpsertAsync(NewCandidate($"{Cycle}|{DealerUrn}|ptp|20990101", new DateOnly(2099, 1, 1), 1m, "Captured", confirmed: false), CancellationToken.None);
        committed = await repoB.ListCommittedByCycleAsync(Cycle, CancellationToken.None);
        Assert.DoesNotContain(committed, c => c.RecordId.EndsWith("20990101", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Due_query_includes_overdue_excludes_future_and_same_day()
    {
        var asOf = new DateOnly(2026, 3, 15);
        await using (var lifeA = CreateLifetime())
        {
            var repo = lifeA.GetRequiredService<IPtpRecordRepository>();
            await repo.UpsertAsync(NewCandidate($"{Cycle}|{DealerUrn}|ptp|20260301", new DateOnly(2026, 3, 1), 1m, "Confirmed", true), CancellationToken.None);
            await repo.UpsertAsync(NewCandidate($"{Cycle}|{DealerUrn}|ptp|20260315", new DateOnly(2026, 3, 15), 1m, "Confirmed", true), CancellationToken.None);
            await repo.UpsertAsync(NewCandidate($"{Cycle}|{DealerUrn}|ptp|20260320", new DateOnly(2026, 3, 20), 1m, "Confirmed", true), CancellationToken.None);
        }

        await using var lifeB = CreateLifetime();
        var due = await lifeB.GetRequiredService<IPtpRecordRepository>().ListDueConfirmedAsync(asOf, CancellationToken.None);
        Assert.Contains(due, d => d.CommitmentDate == new DateOnly(2026, 3, 1));
        Assert.DoesNotContain(due, d => d.CommitmentDate == new DateOnly(2026, 3, 15));
        Assert.DoesNotContain(due, d => d.CommitmentDate == new DateOnly(2026, 3, 20));
    }

    [Fact]
    public async Task Chase_insert_once_and_concurrent_inserts_single_row()
    {
        var chaseId = $"{Cycle}|{DealerUrn}|ptp|20260301|Broken|20260301";
        var entity = NewChase(chaseId, $"{Cycle}|{DealerUrn}|ptp|20260301", new DateOnly(2026, 3, 1), "SuppressedShadow");

        await using (var lifeA = CreateLifetime())
        {
            var chaseRepo = lifeA.GetRequiredService<IPtpChaseRepository>();
            Assert.True(await chaseRepo.TryInsertAsync(entity, CancellationToken.None));
            Assert.False(await chaseRepo.TryInsertAsync(entity, CancellationToken.None));
        }

        await using var lifeB = CreateLifetime();
        var chaseRepoB = lifeB.GetRequiredService<IPtpChaseRepository>();
        var tasks = Enumerable.Range(0, 10).Select(_ => chaseRepoB.TryInsertAsync(entity, CancellationToken.None));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.False(r));
        var listed = await chaseRepoB.ListByCycleAsync(Cycle, CancellationToken.None);
        Assert.Single(listed.Where(c => c.ChaseId == chaseId));
        Assert.Null(listed[0].GetType().GetProperty("Amount"));
    }

    [Fact]
    public async Task Scanner_after_restart_does_not_duplicate_chase()
    {
        var commit = new DateOnly(2026, 1, 1);
        var recordId = $"{Cycle}|{DealerUrn}|ptp|20260101";
        await using (var lifeA = CreateLifetime())
        {
            await lifeA.GetRequiredService<IPtpRecordRepository>().UpsertAsync(
                NewCandidate(recordId, commit, 9_000m, "Confirmed", true), CancellationToken.None);
            var scanner = CreateScanner(lifeA);
            var first = await scanner.ScanDueAsync(new DateOnly(2026, 3, 1), RunMode.Shadow, new CorrelationId("corr-a"), CancellationToken.None);
            Assert.Single(first);
            Assert.Equal(PtpChaseStatus.SuppressedShadow, first[0].Status);
        }

        await using var lifeB = CreateLifetime();
        var second = await CreateScanner(lifeB).ScanDueAsync(new DateOnly(2026, 3, 1), RunMode.Shadow, new CorrelationId("corr-b"), CancellationToken.None);
        Assert.Empty(second);
        var all = await lifeB.GetRequiredService<IPtpChaseRepository>().ListByCycleAsync(Cycle, CancellationToken.None);
        Assert.Single(all.Where(c => c.PtpId == recordId));
    }

    [Fact]
    public async Task Visit_plan_persists_ordered_lines_transactionally()
    {
        var planId = $"{Cycle}|tsi.west@paintco.local";
        await using (var lifeA = CreateLifetime())
        {
            var plans = lifeA.GetRequiredService<IVisitPlanRepository>();
            await plans.UpsertAsync(new VisitPlanEntity
            {
                PlanId = planId,
                CycleId = Cycle,
                TsiId = "tsi.west@paintco.local",
                PlanDate = new DateOnly(2026, 3, 1),
                CorrelationId = "corr-plan",
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Lines =
                [
                    Line(planId, "dealer:b", 2),
                    Line(planId, "dealer:a", 1)
                ]
            }, CancellationToken.None);

            await plans.UpsertAsync(new VisitPlanEntity
            {
                PlanId = planId,
                CycleId = Cycle,
                TsiId = "tsi.west@paintco.local",
                PlanDate = new DateOnly(2026, 3, 1),
                CorrelationId = "corr-plan-2",
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Lines = [Line(planId, "dealer:a", 1), Line(planId, DealerUrn, 2)]
            }, CancellationToken.None);
        }

        await using var lifeB = CreateLifetime();
        var loaded = await lifeB.GetRequiredService<IVisitPlanRepository>().GetAsync(planId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Lines.Count);
        Assert.Equal(1, loaded.Lines[0].Sequence);
        Assert.Equal("dealer:a", loaded.Lines[0].DealerUrn);
        Assert.Equal(DealerUrn, loaded.Lines[1].DealerUrn);
        Assert.Equal("corr-plan-2", loaded.CorrelationId);
    }

    [Fact]
    public async Task Dual_store_tools_adapters_share_sql_sor()
    {
        var recordId = $"{Cycle}|{DealerUrn}|ptp|20260201";
        await using (var lifeA = CreateLifetime())
        {
            var facade = new PtpRepositoryCandidateStore(new SqlBackedPtpRepository(lifeA.GetRequiredService<IPtpRecordRepository>()));
            await facade.SaveAsync(new PtpCandidateRecord
            {
                RecordId = recordId,
                DealerUrn = DealerUrn,
                CycleId = Cycle,
                CommitmentDate = new DateOnly(2026, 2, 1),
                Amount = 3_000m,
                Locale = "en-IN",
                Status = PtpCandidateStatus.Confirmed,
                ConfirmedUtc = DateTimeOffset.UtcNow,
                ConfirmedByUpn = "tsi.west@paintco.local",
                Committed = new PromiseToPay(new DealerUrn(DealerUrn), new DateOnly(2026, 2, 1), new Money(3_000m), true)
            }, CancellationToken.None);
        }

        await using var lifeB = CreateLifetime();
        var repo = new SqlBackedPtpRepository(lifeB.GetRequiredService<IPtpRecordRepository>());
        var loaded = await repo.GetCandidateAsync(recordId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(loaded!.Committed!.ConfirmedByTsi);
        Assert.Equal(3_000m, loaded.Amount);
    }

    [Fact]
    public async Task Sql_failure_fails_closed()
    {
        var services = new ServiceCollection();
        services.AddIntegrationSqlRepositories(
            "Server=invalid-host-that-does-not-exist,1433;Database=x;User Id=x;Password=x;TrustServerCertificate=True;Connect Timeout=1;");
        await using var provider = services.BuildServiceProvider();
        var repo = provider.GetRequiredService<IPtpRecordRepository>();
        await Assert.ThrowsAsync<DataAccessException>(() =>
            repo.GetAsync("missing", CancellationToken.None));
    }

    private PtpChaseScanner CreateScanner(ServiceProvider sp)
        => new(
            new SqlBackedPtpRepository(sp.GetRequiredService<IPtpRecordRepository>()),
            new SqlBackedPtpChaseStore(sp.GetRequiredService<IPtpChaseRepository>()),
            sp.GetRequiredService<IDealerRepository>(),
            NullLogger<PtpChaseScanner>.Instance);

    private ServiceProvider CreateLifetime()
    {
        var services = new ServiceCollection();
        services.AddIntegrationSqlRepositories(_sql.ConnectionString);
        return services.BuildServiceProvider();
    }

    private static PtpRecordEntity NewCandidate(
        string recordId,
        DateOnly commitment,
        decimal amount,
        string status,
        bool confirmed,
        DateTimeOffset? confirmedUtc = null,
        string? confirmedByUpn = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new PtpRecordEntity
        {
            RecordId = recordId,
            CycleId = Cycle,
            DealerUrn = DealerUrn,
            CommitmentDate = commitment,
            Amount = amount,
            Currency = "INR",
            Status = status,
            ConfirmedByTsi = confirmed,
            ConfirmedUtc = confirmed ? confirmedUtc ?? now : null,
            ConfirmedByUpn = confirmed ? confirmedByUpn ?? "tsi.west@paintco.local" : null,
            RequiresTsiConfirmation = !confirmed,
            Discarded = false,
            Locale = "en-IN",
            SpeechConfidence = 0.9m,
            TranscriptSha256 = "ABC123",
            RecognitionStatus = "Succeeded",
            CorrelationId = "corr-sp7",
            CreatedUtc = now,
            UpdatedUtc = now
        };
    }

    private static PtpChaseEntity NewChase(string chaseId, string ptpId, DateOnly date, string status) => new()
    {
        ChaseId = chaseId,
        PtpId = ptpId,
        CycleId = Cycle,
        DealerUrn = DealerUrn,
        OwnerTsi = "tsi.west@paintco.local",
        CommitmentDate = date,
        DueDate = date,
        ChaseType = "Broken",
        Status = status,
        CorrelationId = "corr-chase",
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static VisitPlanLineEntity Line(string planId, string dealer, int sequence) => new()
    {
        PlanId = planId,
        DealerUrn = dealer,
        Sequence = sequence,
        PriorityRank = sequence,
        GeoClusterId = $"{Cycle}|tsi.west@paintco.local|West|Mumbai-Andheri",
        Reason = "PostNotice",
        Status = "SuppressedShadow",
        VisitTaskId = $"{Cycle}|{dealer}|visit",
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private async Task SeedDealerAsync()
    {
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Dealer WHERE Urn = @Urn)
            INSERT INTO dbo.Dealer (Urn, SapCode, PortalId, Depot, Region, CoveringTsi, UnderInsolvencyMoratorium)
            VALUES (@Urn, N'SAP-SP7', N'PORTAL-SP7', N'Mumbai-Andheri', N'West', N'tsi.west@paintco.local', 0);
            """;
        cmd.Parameters.AddWithValue("@Urn", DealerUrn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CleanupAsync()
    {
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF OBJECT_ID(N'dbo.PtpChase', N'U') IS NOT NULL DELETE FROM dbo.PtpChase WHERE DealerUrn = @Urn OR CycleId = @Cycle;
            IF OBJECT_ID(N'dbo.VisitPlanLine', N'U') IS NOT NULL
                DELETE FROM dbo.VisitPlanLine WHERE PlanId LIKE @Cycle + N'|%';
            IF OBJECT_ID(N'dbo.VisitPlan', N'U') IS NOT NULL DELETE FROM dbo.VisitPlan WHERE CycleId = @Cycle;
            IF OBJECT_ID(N'dbo.PtpRecord', N'U') IS NOT NULL DELETE FROM dbo.PtpRecord WHERE CycleId = @Cycle OR DealerUrn = @Urn;
            """;
        cmd.Parameters.AddWithValue("@Urn", DealerUrn);
        cmd.Parameters.AddWithValue("@Cycle", Cycle);
        await cmd.ExecuteNonQueryAsync();
    }
}
