using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ARC.Data.Sql.StoredProcedures;

namespace ARC.Data.DependencyInjection;

public static class ArcSqlStoredProcedureServiceCollectionExtensions
{
    /// <summary>
    /// Registers allow-listed <see cref="StoredProcedureExecutor"/> for ARC-owned repository SP calls.
    /// </summary>
    public static IServiceCollection AddArcSqlStoredProcedureInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<SqlStoredProcedureNames>(
                configuration.GetSection(SqlStoredProcedureNames.SectionName));
            services.Configure<SqlStoredProcedureOptions>(
                configuration.GetSection(SqlStoredProcedureOptions.SectionName));
        }
        else
        {
            services.Configure<SqlStoredProcedureNames>(_ => { });
            services.Configure<SqlStoredProcedureOptions>(_ => { });
        }

        services.AddSingleton<IStoredProcedureExecutor, StoredProcedureExecutor>();
        return services;
    }
}
