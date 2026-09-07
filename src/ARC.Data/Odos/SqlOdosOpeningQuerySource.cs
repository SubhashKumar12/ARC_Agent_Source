using Microsoft.Extensions.Options;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Odos;

namespace ARC.Data.Odos;

/// <summary>
/// Production ODOS opening-data adapter. Executes only the configured, allow-listed
/// stored procedure through <see cref="StoredProcedureExecutor"/>.
/// No inline SQL, no literal procedure name, no connection handling in this class.
/// </summary>
internal sealed class SqlOdosOpeningQuerySource : IOdosOpeningQuerySource
{
    private readonly IStoredProcedureExecutor _executor;
    private readonly SqlStoredProcedureNames _names;

    public SqlOdosOpeningQuerySource(
        IStoredProcedureExecutor executor,
        IOptions<SqlStoredProcedureNames> names)
    {
        _executor = executor;
        _names = names.Value;
    }

    public async Task<IReadOnlyList<OdosOpeningSnapshot>> QueryAsync(
        OdosOpeningQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await _executor.QueryAsync<OdosOpeningRow>(
            _names.GetOdosOpeningData,
            new
            {
                Year = query.YearParameter,
                Month = query.MonthParameter,
                DepotCode = query.DepotCode,
                DealerCode = query.DealerCode
            },
            correlationId: null,
            cancellationToken);

        return rows
            .Select(r => r.ToDomain(query.Year, query.Month))
            .ToList();
    }
}
