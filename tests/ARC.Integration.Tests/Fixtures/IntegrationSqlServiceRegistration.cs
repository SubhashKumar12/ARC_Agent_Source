using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.DependencyInjection;
using ARC.Data.Odos;
using ARC.Data.Sql;

namespace ARC.Integration.Tests.Fixtures;

internal static class IntegrationSqlServiceRegistration
{
    public static IServiceCollection AddIntegrationSqlRepositories(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Options.Create(new ArcDataOptions
        {
            Sql = new SqlStoreOptions
            {
                ConnectionString = connectionString,
                UseManagedIdentity = false
            }
        }));
        services.AddArcSqlStoredProcedureInfrastructure();
        services.Configure<OdosDealerKeyOptions>(_ => { });
        services.AddSingleton<IOdosDealerKeyResolver, OdosDealerKeyResolver>();
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<DealerRepository>();
        services.AddSingleton<IDealerRepository, IntegrationSqlDealerRepository>();
        services.AddSingleton<IDealerIdentityMappingRepository, DealerIdentityMappingRepository>();
        services.AddSingleton<IRecoveryCaseRepository, RecoveryCaseRepository>();
        services.AddSingleton<IGateDecisionRepository, GateDecisionRepository>();
        services.AddSingleton<IPtpRecordRepository, SqlPtpRecordRepository>();
        services.AddSingleton<IVisitPlanRepository, SqlVisitPlanRepository>();
        services.AddSingleton<IPtpChaseRepository, SqlPtpChaseRepository>();
        return services;
    }
}
