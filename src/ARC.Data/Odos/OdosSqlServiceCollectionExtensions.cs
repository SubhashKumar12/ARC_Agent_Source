using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ARC.Data.Configuration;
using ARC.Data.Sql;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Odos;

namespace ARC.Data.Odos;

public static class OdosSqlServiceCollectionExtensions
{
    /// <summary>
    /// Registers the production ODOS stored-procedure read path: central connection factory,
    /// stored procedure executor, opening-data source, business-limit source, diagnostics and dry run.
    /// Read-only — no write capability is registered.
    /// </summary>
    public static IServiceCollection AddArcOdosSqlReadPath(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ArcDataOptions>(configuration.GetSection(ArcDataOptions.SectionName));
        services.Configure<OdosSqlConnectionOptions>(configuration.GetSection(OdosSqlConnectionOptions.SectionName));
        services.Configure<SqlStoredProcedureNames>(configuration.GetSection(SqlStoredProcedureNames.SectionName));
        services.Configure<SqlStoredProcedureOptions>(configuration.GetSection(SqlStoredProcedureOptions.SectionName));

        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<IStoredProcedureExecutor, StoredProcedureExecutor>();

        services.AddSingleton<IOdosOpeningQuerySource, SqlOdosOpeningQuerySource>();
        services.AddSingleton<IOdosBusinessLineLimitSource, SqlOdosBusinessLineLimitSource>();
        services.AddSingleton<IOdosSqlDiagnostics, OdosSqlDiagnostics>();
        services.AddSingleton<IOdosDryRunService, OdosDryRunService>();

        return services;
    }
}
