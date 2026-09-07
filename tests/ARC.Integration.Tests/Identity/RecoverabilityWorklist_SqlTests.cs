using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Integration.Tests.Fixtures;

namespace ARC.Integration.Tests.Identity;

/// <summary>SP3 SQL ranked worklist. Cosmos is not used. Separate from AC#2.</summary>
[Trait("Category", "Integration")]
[Collection("Sql-Schema")]
public sealed class RecoverabilityWorklist_SqlTests : IAsyncLifetime
{
    private readonly SqlFixture _sql = new();
    private const string Cycle = "2026-03-sp3w";
    private static readonly string[] Dealers = ["dealer:sp3-a", "dealer:sp3-b", "dealer:sp3-c", "dealer:sp3-blocked", "dealer:sp3-alpha", "dealer:sp3-zeta"];

    public async Task InitializeAsync()
    {
        await InfrastructureGate.EnsureSqlAvailableAsync(CancellationToken.None);
        await _sql.InitializeAsync(CancellationToken.None);
        await CleanupAsync();
        await using var lifetime = CreateLifetime(_sql.ConnectionString);
        var cases = lifetime.GetRequiredService<IRecoveryCaseRepository>();
        await InsertDealerAsync("dealer:sp3-a");
        await InsertDealerAsync("dealer:sp3-b");
        await InsertDealerAsync("dealer:sp3-c");
        await InsertDealerAsync("dealer:sp3-blocked");
        await InsertDealerAsync("dealer:sp3-alpha");
        await InsertDealerAsync("dealer:sp3-zeta");
        await cases.UpsertIndexAsync(Row("dealer:sp3-a", 330_000m, WorkflowStatus.Running), CancellationToken.None);
        await cases.UpsertIndexAsync(Row("dealer:sp3-b", 100_000m, WorkflowStatus.Running), CancellationToken.None);
        await cases.UpsertIndexAsync(Row("dealer:sp3-c", 5_001m, WorkflowStatus.Running), CancellationToken.None);
        await cases.UpsertIndexAsync(Row("dealer:sp3-blocked", 999_999m, WorkflowStatus.Blocked), CancellationToken.None);
        await cases.UpsertIndexAsync(Row("dealer:sp3-alpha", 50_000m, WorkflowStatus.Running), CancellationToken.None);
        await cases.UpsertIndexAsync(Row("dealer:sp3-zeta", 50_000m, WorkflowStatus.Running), CancellationToken.None);
    }

    public Task DisposeAsync() => CleanupAsync();

    [Fact]
    public async Task Sql_worklist_ranks_score_desc_then_urn_ordinal_and_excludes_blocked()
    {
        await using var lifetime = CreateLifetime(_sql.ConnectionString);
        var cases = lifetime.GetRequiredService<IRecoveryCaseRepository>();
        var list = await cases.ListRankedWorklistAsync(new CycleId(Cycle), null, null, topDecile: false, CancellationToken.None);

        Assert.Equal(["dealer:sp3-a", "dealer:sp3-b", "dealer:sp3-alpha", "dealer:sp3-zeta", "dealer:sp3-c"],
            list.Entries.Select(e => e.DealerUrn.Value).ToArray());
        Assert.Equal([330_000m, 100_000m, 50_000m, 50_000m, 5_001m], list.Entries.Select(e => e.RecoverabilityScore).ToArray());
        Assert.DoesNotContain(list.Entries, e => e.DealerUrn.Value == "dealer:sp3-blocked");
        Assert.Equal(5, list.EligibleCount);
        Assert.Equal(1, RecoverabilityWorklistTop(list.EligibleCount));
    }

    [Fact]
    public async Task Sql_top_decile_uses_ceiling_ten_percent()
    {
        await using var lifetime = CreateLifetime(_sql.ConnectionString);
        var cases = lifetime.GetRequiredService<IRecoveryCaseRepository>();
        var list = await cases.ListRankedWorklistAsync(new CycleId(Cycle), null, null, topDecile: true, CancellationToken.None);

        Assert.Equal(5, list.EligibleCount);
        Assert.Single(list.Entries);
        Assert.Equal("dealer:sp3-a", list.Entries[0].DealerUrn.Value);
        Assert.Equal(330_000m, list.Entries[0].RecoverabilityScore);
    }

    private static int RecoverabilityWorklistTop(int eligible)
        => ARC.Domain.Metrics.RecoverabilityWorklist.TopDecileSize(eligible);

    private static RecoveryCaseIndex Row(string urn, decimal score, WorkflowStatus status)
        => new(
            new CycleId(Cycle),
            new DealerUrn(urn),
            status.ToString(),
            "corr-sp3",
            null,
            DateTimeOffset.UtcNow,
            score,
            nameof(RecoveryTier.Notice));

    private static ServiceProvider CreateLifetime(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddIntegrationSqlRepositories(connectionString);
        return services.BuildServiceProvider();
    }

    private async Task InsertDealerAsync(string urn)
    {
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Dealer WHERE Urn = @Urn)
            INSERT INTO dbo.Dealer (Urn, Depot, Region, CoveringTsi, UnderInsolvencyMoratorium)
            VALUES (@Urn, N'Mumbai-Andheri', N'West', N'tsi.west@paintco.local', 0);
            """;
        cmd.Parameters.AddWithValue("@Urn", urn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CleanupAsync()
    {
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        foreach (var urn in Dealers)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.RecoveryCaseIndex', N'U') IS NOT NULL
                    DELETE FROM dbo.RecoveryCaseIndex WHERE DealerUrn = @Urn;
                IF OBJECT_ID(N'dbo.DealerSourceIdentifier', N'U') IS NOT NULL
                    DELETE FROM dbo.DealerSourceIdentifier WHERE CanonicalUrn = @Urn;
                IF OBJECT_ID(N'dbo.DealerAlias', N'U') IS NOT NULL
                    DELETE FROM dbo.DealerAlias WHERE CanonicalUrn = @Urn;
                DELETE FROM dbo.LedgerPosition WHERE DealerUrn = @Urn;
                DELETE FROM dbo.Dealer WHERE Urn = @Urn;
                """;
            cmd.Parameters.AddWithValue("@Urn", urn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
