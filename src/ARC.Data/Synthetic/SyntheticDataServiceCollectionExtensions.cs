using Microsoft.Extensions.DependencyInjection;
using ARC.Data.A1;
using ARC.Data.Sql;
using ARC.Domain.Odos;

namespace ARC.Data.Synthetic;

public static class SyntheticDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the synthetic assignment dataset as the A1/SQL repository backends.
    /// For evaluation only — does not replace production AddArcData wiring.
    /// </summary>
    public static IServiceCollection AddArcSyntheticAssignmentData(
        this IServiceCollection services,
        SyntheticDatasetOptions? options = null)
    {
        options ??= new SyntheticDatasetOptions();
        var dataset = new SyntheticDatasetGenerator().Generate(options);
        var store = new SyntheticDatasetStore(dataset);

        services.AddSingleton(dataset);
        services.AddSingleton(store);
        services.AddSingleton<IDealerRepository>(store);
        services.AddSingleton<ILedgerRepository>(store);
        services.AddSingleton<IChequeRepository>(store);
        services.AddSingleton<IOdosOpeningDataSource>(store);
        services.AddSingleton<ILedgerAdjustmentFactSource>(store);
        services.AddSingleton<IBusinessLineLimitProvider>(_ =>
            new ConfigurableBusinessLineLimitProvider(new BusinessLineLimitOptions
            {
                DefaultLimit = options.DefaultBusinessLimit
            }));

        return services;
    }
}
