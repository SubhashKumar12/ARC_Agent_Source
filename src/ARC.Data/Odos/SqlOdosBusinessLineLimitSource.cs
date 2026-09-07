using Microsoft.Extensions.Options;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Odos;

namespace ARC.Data.Odos;

/// <summary>
/// Production business-limit adapter. The stored procedure remains authoritative for
/// <c>fn_GetBusinessLineLimit</c> and its default fallback — that logic is not duplicated here.
/// </summary>
internal sealed class SqlOdosBusinessLineLimitSource : IOdosBusinessLineLimitSource
{
    private readonly IStoredProcedureExecutor _executor;
    private readonly SqlStoredProcedureNames _names;

    public SqlOdosBusinessLineLimitSource(
        IStoredProcedureExecutor executor,
        IOptions<SqlStoredProcedureNames> names)
    {
        _executor = executor;
        _names = names.Value;
    }

    public async Task<OdosBusinessLineLimit?> GetLimitAsync(
        string? businessLine,
        CancellationToken cancellationToken)
    {
        var row = await _executor.QuerySingleOrDefaultAsync<OdosBusinessLimitRow>(
            _names.GetBusinessLineLimit,
            new { BusinessLine = string.IsNullOrWhiteSpace(businessLine) ? null : businessLine.Trim() },
            correlationId: null,
            cancellationToken);

        return row?.ToDomain();
    }
}

/// <summary>
/// Adapts the async SQL limit source to the synchronous A1 <see cref="IBusinessLineLimitProvider"/>
/// contract by prefetching approved limits. Keeps A1 formulas and call shapes unchanged and
/// avoids sync-over-async inside rule evaluation.
/// </summary>
public sealed class PrefetchedOdosBusinessLineLimitProvider : IBusinessLineLimitProvider
{
    private readonly Dictionary<string, decimal> _byBusinessLine;
    private readonly decimal _defaultLimit;

    private PrefetchedOdosBusinessLineLimitProvider(
        Dictionary<string, decimal> byBusinessLine,
        decimal defaultLimit)
    {
        _byBusinessLine = byBusinessLine;
        _defaultLimit = defaultLimit;
    }

    /// <summary>
    /// Loads the SQL default limit plus any requested business lines.
    /// The default comes from the stored procedure invoked with a NULL business line.
    /// </summary>
    public static async Task<PrefetchedOdosBusinessLineLimitProvider> CreateAsync(
        IOdosBusinessLineLimitSource source,
        IEnumerable<string?> businessLines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var fallback = await source.GetLimitAsync(null, cancellationToken)
            ?? throw new InvalidOperationException(
                "The approved business-limit stored procedure returned no default limit.");

        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in businessLines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var limit = await source.GetLimitAsync(line, cancellationToken);
            if (limit is not null)
                map[line] = limit.BusinessLimit;
        }

        return new PrefetchedOdosBusinessLineLimitProvider(map, fallback.BusinessLimit);
    }

    public decimal? GetBusinessLineLimit(string? businessLine)
    {
        if (string.IsNullOrWhiteSpace(businessLine))
            return null;
        return _byBusinessLine.TryGetValue(businessLine.Trim(), out var limit) ? limit : null;
    }

    public decimal GetDefaultLimit() => _defaultLimit;
}
