using Dapper;

namespace ARC.Data.Sql.StoredProcedures;

/// <summary>
/// Internal infrastructure seam for stored procedure execution.
/// Public so ARC.Data repositories can consume it via DI; implementations remain internal.
/// No caller outside the data layer should supply a procedure name.
/// </summary>
public interface IStoredProcedureExecutor
{
    Task<T?> QuerySingleOrDefaultAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteAsync(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<T?> ExecuteScalarAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<DynamicParameters> ExecuteWithOutputAsync(
        string configuredProcedureName,
        DynamicParameters parameters,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Read first result set as single-or-default, second as list (multi-grid SPs).</summary>
    Task<(TFirst?, IReadOnlyList<TSecond>)> QuerySingleAndListAsync<TFirst, TSecond>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Read two result sets as lists (multi-grid SPs).</summary>
    Task<(IReadOnlyList<TFirst>, IReadOnlyList<TSecond>)> QueryListAndListAsync<TFirst, TSecond>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
