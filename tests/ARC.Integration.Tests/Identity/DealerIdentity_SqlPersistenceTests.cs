using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;
using ARC.Integration.Tests.Fixtures;
using ARC.Tools.Identity;

namespace ARC.Integration.Tests.Identity;

/// <summary>
/// SP2 SQL identity persistence across two DI lifetimes. Cosmos is not used.
/// Separate from AC#2 cold-resume tests.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sql-Schema")]
public sealed class DealerIdentity_SqlPersistenceTests : IAsyncLifetime
{
    private readonly SqlFixture _sql = new();
    private const string Urn = "dealer:sp2-persist";
    private const string TradeName = "M/S Jai Bhavani Hardware";

    public async Task InitializeAsync()
    {
        await InfrastructureGate.EnsureSqlAvailableAsync(CancellationToken.None);
        await _sql.InitializeAsync(CancellationToken.None);
        await DeleteDealerAsync(Urn);
        await InsertDealerAsync(Urn);
        await InsertAliasAsync(Urn, DealerAliasKinds.TradeLegalName, TradeName);
    }

    public async Task DisposeAsync() => await DeleteDealerAsync(Urn);

    [Fact]
    public async Task Mapping_survives_host_lifetime_A_to_B_for_sap_and_portal()
    {
        DealerUrn sapUrn;
        DealerUrn portalUrn;

        await using (var lifetimeA = CreateLifetime(_sql.ConnectionString))
        {
            var resolver = lifetimeA.GetRequiredService<ResolveDealerIdentityTool>();
            var sap = await resolver.ResolveAsync(
                new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-4411", tradeLegalName: TradeName),
                CancellationToken.None);
            var portal = await resolver.ResolveAsync(
                new SourceDealerRecord(DealerSourceSystems.Portal, "PORTAL-8822", tradeLegalName: TradeName),
                CancellationToken.None);

            Assert.Equal(DealerIdentityStatus.Resolved, sap.Status);
            Assert.Equal(DealerIdentityStatus.Resolved, portal.Status);
            Assert.Equal(Urn, sap.CanonicalUrn?.Value);
            Assert.Equal(Urn, portal.CanonicalUrn?.Value);
            sapUrn = sap.CanonicalUrn ?? throw new InvalidOperationException("SAP resolve missing URN.");
            portalUrn = portal.CanonicalUrn ?? throw new InvalidOperationException("Portal resolve missing URN.");
        }

        await using (var lifetimeB = CreateLifetime(_sql.ConnectionString))
        {
            var resolver = lifetimeB.GetRequiredService<ResolveDealerIdentityTool>();
            var sap = await resolver.ResolveAsync(
                new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-4411"), CancellationToken.None);
            var portal = await resolver.ResolveAsync(
                new SourceDealerRecord(DealerSourceSystems.Portal, "PORTAL-8822"), CancellationToken.None);

            Assert.Equal(DealerIdentityStatus.Resolved, sap.Status);
            Assert.Equal(DealerIdentityStatus.Resolved, portal.Status);
            Assert.Equal(DealerMatchKind.ExistingMapping, sap.MatchKind);
            Assert.Equal(DealerMatchKind.ExistingMapping, portal.MatchKind);
            Assert.Equal(sapUrn.Value, sap.CanonicalUrn?.Value);
            Assert.Equal(portalUrn.Value, portal.CanonicalUrn?.Value);
            Assert.Equal(Urn, sap.CanonicalUrn?.Value);
            Assert.Equal(Urn, portal.CanonicalUrn?.Value);
        }
    }

    private static ServiceProvider CreateLifetime(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntegrationSqlRepositories(connectionString);
        services.AddSingleton<ResolveDealerIdentityTool>();
        return services.BuildServiceProvider();
    }

    private async Task InsertDealerAsync(string urn)
    {
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dbo.Dealer (Urn, Depot, Region, CoveringTsi, UnderInsolvencyMoratorium)
            VALUES (@Urn, N'Mumbai-Andheri', N'West', N'tsi.west@paintco.local', 0);
            """;
        cmd.Parameters.AddWithValue("@Urn", urn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertAliasAsync(string urn, string kind, string alias)
    {
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dbo.DealerAlias (CanonicalUrn, AliasKind, AliasValue)
            VALUES (@Urn, @Kind, @Alias);
            """;
        cmd.Parameters.AddWithValue("@Urn", urn);
        cmd.Parameters.AddWithValue("@Kind", kind);
        cmd.Parameters.AddWithValue("@Alias", DealerIdentityNormalization.Alias(alias));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task DeleteDealerAsync(string urn)
    {
        await using var connection = new SqlConnection(_sql.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
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
