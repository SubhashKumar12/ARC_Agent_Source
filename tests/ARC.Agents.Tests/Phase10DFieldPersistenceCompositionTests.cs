using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ARC.Data.Configuration;
using ARC.Data.DependencyInjection;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;
using ARC.Tools.DependencyInjection;
using ARC.Tools.Field;
using ARC.Tools.Persistence;

namespace ARC.Agents.Tests;

public sealed class Phase10DFieldPersistenceCompositionTests
{
    [Fact]
    public void InMemory_mode_candidate_store_and_repository_share_same_instance()
    {
        using var provider = Build(sqlConnection: null);
        var memory = provider.GetRequiredService<InMemoryPtpRepository>();
        var asInterface = provider.GetRequiredService<IPtpRepository>();
        var facade = Assert.IsType<PtpRepositoryCandidateStore>(provider.GetRequiredService<IPtpCandidateStore>());

        Assert.Same(memory, asInterface);
        Assert.NotNull(facade);
        Assert.Same(memory, provider.GetRequiredService<IPtpRepository>());
    }

    [Fact]
    public async Task Capture_through_facade_is_visible_to_repository()
    {
        using var provider = Build(sqlConnection: null);
        var facade = provider.GetRequiredService<IPtpCandidateStore>();
        var repo = provider.GetRequiredService<IPtpRepository>();

        await facade.SaveAsync(new PtpCandidateRecord
        {
            RecordId = "c1|d1|ptp|20260101",
            DealerUrn = "dealer:d1",
            CycleId = "c1",
            Locale = "en-IN",
            Status = PtpCandidateStatus.Captured,
            Amount = 10m,
            CommitmentDate = new DateOnly(2026, 1, 1)
        }, CancellationToken.None);

        var loaded = await repo.GetCandidateAsync("c1|d1|ptp|20260101", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(PtpCandidateStatus.Captured, loaded!.Status);
    }

    [Fact]
    public void Sql_mode_registers_sql_backed_adapters()
    {
        using var provider = Build(sqlConnection: "Server=(localdb)\\MSSQLLocalDB;Database=ARC_DoesNotNeedToExist;Trusted_Connection=True;TrustServerCertificate=True;");
        Assert.IsType<SqlBackedPtpRepository>(provider.GetRequiredService<IPtpRepository>());
        Assert.IsType<PtpRepositoryCandidateStore>(provider.GetRequiredService<IPtpCandidateStore>());
        Assert.IsType<SqlBackedVisitPlanStore>(provider.GetRequiredService<IVisitPlanStore>());
        Assert.IsType<SqlBackedPtpChaseStore>(provider.GetRequiredService<IPtpChaseStore>());
    }

    [Fact]
    public async Task Production_without_arc_sql_registers_not_configured_stores()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDealerRepository, EmptyDealers>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        services.AddArcTools(configuration);
        using var provider = services.BuildServiceProvider();

        var availability = provider.GetRequiredService<IFieldPersistenceAvailability>();
        Assert.True(availability.IsProductionNotConfigured);
        var repo = provider.GetRequiredService<IPtpRepository>();
        await Assert.ThrowsAsync<FieldPersistenceNotConfiguredException>(() =>
            repo.ListCommittedByDealerAsync(new DealerUrn("odos-chat:005:36949"), CancellationToken.None));
    }

    private static ServiceProvider Build(string? sqlConnection)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDealerRepository, EmptyDealers>();

        var values = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(sqlConnection))
        {
            values["ArcData:Sql:ConnectionString"] = sqlConnection;
            // Data SQL repos required when Tools selects SQL-backed adapters.
            services.AddSingleton<IPtpRecordRepository, SqlPtpRecordRepository>();
            services.AddSingleton<IVisitPlanRepository, SqlVisitPlanRepository>();
            services.AddSingleton<IPtpChaseRepository, SqlPtpChaseRepository>();
            services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
            services.AddArcSqlStoredProcedureInfrastructure();
            services.AddOptions<ArcDataOptions>()
                .Configure(o => o.Sql.ConnectionString = sqlConnection);
        }
        else
        {
            values["ArcTools:FieldPersistence:UseInMemory"] = "true";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddArcTools(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class EmptyDealers : IDealerRepository
    {
        public Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken) => Task.FromResult<Dealer?>(null);
        public Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Dealer>>([]);
        public Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Dealer>>([]);
    }
}
